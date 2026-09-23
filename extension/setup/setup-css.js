/* The guided setup's stylesheet, as a string: the page after install links it in, and the content
 * script drops it into the overlay's shadow root (a content script cannot link a stylesheet). */
self.BCV_SETUP_CSS = String.raw`
/* The guided setup page (the "Simpl Courses Setup" mockup): a glass card over drifting colour,
 * one question per step, every transition eased. Colours follow the appearance the extension
 * chose (data-theme on <html>), else the system. */
:root, :host {
  --bg: #ececf1; --ink: #1c1c1e; --ink2: #3c3c43; --ink3: #6c6c70;
  --card: rgba(255,255,255,.6); --glass: rgba(255,255,255,.7); --fill: rgba(118,118,128,.13);
  --sep: rgba(60,60,67,.08); --edge: rgba(60,60,67,.09); --shadow-a: .12; --blob-a: .34; --blob-b: .26;
  --green: #1a6b30; --warn: #8a5200; --blue: #0a84ff; --sel-bg: rgba(10,132,255,.06); --seg-on: #fff; --knob-edge: rgba(60,60,67,.22);
  --font: -apple-system, BlinkMacSystemFont, "SF Pro Text", "Helvetica Neue", Helvetica, Arial, sans-serif;
  --display: -apple-system, BlinkMacSystemFont, "SF Pro Display", "Helvetica Neue", Helvetica, Arial, sans-serif;
  --mono: ui-monospace, Menlo, monospace;
  --ease: cubic-bezier(.32,.72,0,1);
  --spring: cubic-bezier(.34,1.3,.42,1);
}
html[data-theme="dark"], :host([data-theme="dark"]) {
  --bg: #000; --ink: #f2f2f7; --ink2: #c7c7cc; --ink3: #8e8e93;
  --card: rgba(44,44,46,.5); --glass: rgba(28,28,30,.7); --fill: rgba(118,118,128,.28);
  --sep: rgba(255,255,255,.08); --edge: rgba(255,255,255,.1); --shadow-a: .5; --blob-a: .24; --blob-b: .18;
  --green: #5ddb7d; --warn: #ffb44d; --sel-bg: rgba(10,132,255,.13); --seg-on: #48484a; --knob-edge: rgba(255,255,255,.26);
}
* { box-sizing: border-box; }
html, body { min-height: 100%; }
body { margin: 0; background: var(--bg); color: var(--ink); font: 14px/1.45 var(--font); -webkit-font-smoothing: antialiased; overflow-x: hidden; animation: fadeIn .4s ease both; transition: background .4s ease, color .4s ease; }
button { font-family: inherit; color: inherit; }
button:focus-visible, input:focus-visible { outline: 2px solid var(--blue); outline-offset: 2px; }

/* ---- motion ---- */
@keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
@keyframes fadeUp { from { opacity: 0; transform: translateY(12px); } to { opacity: 1; transform: none; } }
@keyframes pop { 0% { opacity: 0; transform: scale(.6); } 70% { transform: scale(1.08); } 100% { opacity: 1; transform: none; } }
@keyframes ripple { from { transform: scale(.6); opacity: .55; } to { transform: scale(1.9); opacity: 0; } }
@keyframes pulse { 0% { opacity: .3; } 50% { opacity: .9; } 100% { opacity: .3; } }
@keyframes spin { to { transform: rotate(360deg); } }
@keyframes sweep { from { transform: scaleX(0); } to { transform: scaleX(1); } }
@keyframes drawStroke { to { stroke-dashoffset: 0; } }
@keyframes driftA { 0% { transform: translate(0,0) scale(1); } 33% { transform: translate(90px,-40px) scale(1.14); } 66% { transform: translate(30px,60px) scale(.96); } 100% { transform: translate(0,0) scale(1); } }
@keyframes driftB { 0% { transform: translate(0,0) scale(1.04); } 33% { transform: translate(-70px,50px) scale(.94); } 66% { transform: translate(-20px,-60px) scale(1.1); } 100% { transform: translate(0,0) scale(1.04); } }
@keyframes driftC { 0% { transform: translate(0,0) scale(1); } 50% { transform: translate(50px,70px) scale(1.16); } 100% { transform: translate(0,0) scale(1); } }
@keyframes driftD { 0% { transform: translate(0,0) scale(1.08); } 50% { transform: translate(-80px,-40px) scale(.92); } 100% { transform: translate(0,0) scale(1.08); } }
@keyframes shake { 0%, 100% { transform: none; } 20% { transform: translateX(-6px); } 40% { transform: translateX(6px); } 60% { transform: translateX(-4px); } 80% { transform: translateX(4px); } }

/* ---- the ground ---- */
.bg { position: fixed; inset: 0; overflow: hidden; pointer-events: none; }
.blob { position: absolute; border-radius: 50%; filter: blur(100px); display: block; will-change: transform; }
.blob--a { top: -200px; left: -140px; width: 560px; height: 560px; background: #34c759; opacity: var(--blob-a); animation: driftA 26s ease-in-out infinite; }
.blob--b { bottom: -240px; right: -160px; width: 620px; height: 620px; background: #5856d6; opacity: var(--blob-a); animation: driftB 31s ease-in-out infinite; filter: blur(110px); }
.blob--c { top: 28%; right: 12%; width: 400px; height: 400px; background: #30b0c7; opacity: var(--blob-b); animation: driftC 37s ease-in-out infinite; }
.blob--d { bottom: 8%; left: 14%; width: 360px; height: 360px; background: #0a84ff; opacity: var(--blob-b); animation: driftD 33s ease-in-out infinite; }

/* ---- the overlay (the same card, drawn over the Canvas page from the content script) ---- */
.overlay { position: fixed; inset: 0; z-index: 2147482500; overflow: hidden; overscroll-behavior: contain; background: rgba(30,30,34,.36); -webkit-backdrop-filter: blur(14px) saturate(1.1); backdrop-filter: blur(14px) saturate(1.1); color: var(--ink); font: 14px/1.45 var(--font); -webkit-font-smoothing: antialiased; animation: fadeIn .35s ease both; transition: opacity .28s ease; }
:host([data-theme="dark"]) .overlay { background: rgba(0,0,0,.5); }
.overlay.is-closing { opacity: 0; }
/* the page never scrolls: the card's body does, between its title and its buttons */
.overlay .page { height: 100%; min-height: 0; box-sizing: border-box; overflow: hidden; }
.overlay .card { display: flex; flex-direction: column; min-height: 0; max-height: calc(100% - 96px); }
.overlay .card__body { flex: 1 1 auto; min-height: 0; overflow-y: auto; overscroll-behavior: contain; margin: 0 -8px; padding: 0 8px; }
.overlay .foot { flex: none; }
.overlay .bg .blob { opacity: calc(var(--blob-a) * .7); }

/* ---- layout ---- */
.page { position: relative; z-index: 1; min-height: 100vh; padding: 32px 24px 52px; display: flex; flex-direction: column; align-items: center; gap: 22px; }
.top { width: 100%; max-width: 560px; display: flex; align-items: center; gap: 12px; animation: fadeUp .5s var(--ease) both; }
.mark { flex: none; border-radius: 8px; background: linear-gradient(158deg, #0a84ff, #0a4fd6); box-shadow: 0 2px 6px rgba(10,79,214,.28); display: flex; align-items: center; justify-content: center; }
.mark--sm { width: 30px; height: 30px; }
.mark--sm svg { display: block; }
.top__name { font: 600 14px/1.2 var(--display); letter-spacing: -.02em; }
.top__step { margin-left: auto; font: 500 12px/1.2 var(--font); color: var(--ink3); transition: opacity .25s ease; }
.top__skip { border: 0; background: transparent; cursor: pointer; font: 500 12px/1.2 var(--font); color: var(--ink3); padding: 6px 2px; transition: color .2s ease, opacity .25s ease; }
.top__skip:hover { color: var(--ink); }
.top__skip[hidden] { display: none; }

.progress { width: 100%; max-width: 560px; display: flex; gap: 5px; animation: fadeUp .5s var(--ease) .05s both; }
.progress span { flex: 1; height: 3px; border-radius: 2px; background: var(--fill); position: relative; overflow: hidden; transition: background .35s var(--ease); }
.progress span::after { content: ""; position: absolute; inset: 0; border-radius: 2px; transform-origin: left; transform: scaleX(0); }
.progress span.is-done { background: #34c759; }
.progress span.is-current { background: var(--fill); }
.progress span.is-current::after { background: var(--blue); animation: sweep .5s var(--ease) both; transform: scaleX(1); }

.card { width: 100%; max-width: 560px; background: var(--glass); -webkit-backdrop-filter: blur(22px) saturate(1.3); backdrop-filter: blur(22px) saturate(1.3); border: 1px solid var(--edge); border-radius: 26px; box-shadow: 0 20px 50px rgba(0,0,0,var(--shadow-a)); padding: 32px 32px 26px; animation: fadeUp .55s var(--ease) .1s both; overflow: hidden; transition: height .42s var(--ease); }
.card.is-shaking { animation: shake .4s ease; }
.card__body { transition: opacity .18s ease, transform .18s ease; }
.card__body.is-leaving { opacity: 0; transform: translateX(var(--leave, -16px)); }
.card__body.is-entering { animation: none; opacity: 0; transform: translateX(var(--enter, 16px)); }
.card__body.is-entered { transition: opacity .36s var(--ease), transform .42s var(--ease); opacity: 1; transform: none; }
.foot { margin-top: 24px; display: flex; align-items: center; gap: 10px; }
.foot__spacer { flex: 1; }
.btn { flex: none; height: 44px; padding: 0 24px; border: 0; border-radius: 22px; background: var(--blue); color: #fff; cursor: pointer; font: 600 15px/1 var(--font); transition: background .25s ease, color .25s ease, transform .18s var(--spring), box-shadow .25s ease; }
.btn:hover { box-shadow: 0 6px 18px rgba(10,132,255,.28); transform: translateY(-1px); }
.btn:active { transform: scale(.97); }
.btn:disabled { background: var(--fill); color: var(--ink3); cursor: default; box-shadow: none; transform: none; }
.btn--ghost { background: transparent; color: var(--ink2); padding: 0 18px; font-size: 14.5px; }
.btn--ghost:hover { background: var(--fill); box-shadow: none; transform: none; }
.btn--quiet { background: transparent; color: var(--ink3); padding: 0 16px; font-size: 14.5px; }
.btn--quiet:hover { color: var(--ink); box-shadow: none; transform: none; }
.btn.is-busy { position: relative; color: transparent; }
.btn.is-busy::after { content: ""; position: absolute; left: 50%; top: 50%; width: 16px; height: 16px; margin: -8px 0 0 -8px; border-radius: 50%; border: 2.5px solid rgba(255,255,255,.35); border-top-color: #fff; animation: spin .8s linear infinite; }

/* ---- steps ---- */
.h1 { margin: 0; font: 700 26px/1.2 var(--display); letter-spacing: -.03em; }
.sub { margin: 7px 0 0; font: 400 13.5px/1.5 var(--font); color: var(--ink3); text-wrap: pretty; }
.welcome { display: flex; flex-direction: column; align-items: center; text-align: center; gap: 14px; padding: 8px 0 4px; }
.mark--lg { width: 96px; height: 96px; border-radius: 24px; box-shadow: 0 12px 30px rgba(10,79,214,.3); animation: pop .6s var(--spring) both; }
.mark--lg svg { display: block; }
.mark--lg .sheet { animation: fadeIn .5s ease .45s both; }
.mark--lg .arc { stroke-dashoffset: var(--len); animation: drawStroke 1s var(--ease) both; }
.mark--lg .arc--2 { animation-duration: .9s; animation-delay: .14s; }
.mark--lg .arc--3 { animation-duration: .8s; animation-delay: .26s; }
.welcome .h1 { font-size: 32px; line-height: 1.12; letter-spacing: -.035em; animation: fadeUp .45s var(--ease) .15s both; }
.welcome .lead { margin: 0; max-width: 330px; font: 400 15px/1.55 var(--font); color: var(--ink2); text-wrap: pretty; animation: fadeUp .45s var(--ease) .25s both; }

.field { margin-top: 18px; display: flex; align-items: center; gap: 10px; padding: 13px 15px; border-radius: 15px; background: var(--card); border: 1px solid var(--edge); transition: border-color .25s ease, box-shadow .25s ease; }
.field.is-ok { border-color: rgba(52,199,89,.5); }
.field.is-bad { border-color: rgba(255,69,58,.5); }
.field:focus-within { box-shadow: 0 0 0 3px rgba(10,132,255,.14); }
.field input { flex: 1; min-width: 0; border: 0; outline: none; background: transparent; font: 500 14.5px/1.3 var(--mono); color: var(--ink); }
.field input::placeholder { color: var(--ink3); opacity: .8; }
.field .tick { flex: none; animation: pop .35s var(--spring) both; }
.field .tick[hidden] { display: none; }
.lines { margin-top: 16px; display: flex; flex-direction: column; gap: 9px; }
.line { display: flex; align-items: center; gap: 10px; font: 400 13px/1.4 var(--font); color: var(--ink2); animation: fadeUp .4s var(--ease) both; }
.line svg { flex: none; }
.line--off { color: var(--ink3); }
.notice { margin-top: 14px; padding: 12px 14px; border-radius: 13px; background: rgba(255,149,0,.12); color: var(--ink); font: 400 13px/1.45 var(--font); display: flex; flex-direction: column; gap: 8px; animation: fadeUp .35s var(--ease) both; }
.notice[hidden] { display: none; }
.notice__t { font-weight: 600; }
.notice__row { display: flex; gap: 8px; flex-wrap: wrap; }
.btn--sm { height: 34px; padding: 0 14px; font-size: 13px; border-radius: 17px; }
.notice--red { background: rgba(255,69,58,.11); border: 1px solid rgba(255,69,58,.28); color: #e5372b; font-weight: 500; }
:host([data-theme="dark"]) .notice--red, html[data-theme="dark"] .notice--red { color: #ff6961; }

/* ---- the page after install: how to start ---- */
.how { margin-top: 22px; display: flex; flex-direction: column; gap: 9px; }
.how__step { display: flex; align-items: flex-start; gap: 14px; padding: 14px 16px; border-radius: 16px; background: var(--card); border: 1px solid var(--edge); animation: fadeUp .42s var(--ease) both; }
.how__n { flex: none; width: 26px; height: 26px; border-radius: 13px; background: var(--blue); color: #fff; font: 600 13px/26px var(--font); text-align: center; }
.how__t { display: block; font: 600 14.5px/1.3 var(--font); color: var(--ink); }
.how__s { display: block; margin-top: 3px; font: 400 12.5px/1.45 var(--font); color: var(--ink3); text-wrap: pretty; }
/* the page after install: each step with a small picture of the thing to find */
.how__step--pic { align-items: flex-start; }
.how__body { flex: 1; min-width: 0; }
.how__step--pic .how__t { font-size: 15.5px; }
.how__step--pic .how__s { font-size: 13px; color: var(--ink2); }
.pic { margin-top: 10px; padding: 12px 14px; border-radius: 14px; background: var(--fill); display: flex; flex-direction: column; align-items: flex-start; gap: 8px; }
.pic__bar { display: inline-flex; align-items: center; gap: 8px; padding: 8px 14px; border-radius: 999px; background: var(--card); border: 1px solid var(--edge); font: 500 12.5px/1 var(--mono); color: var(--ink2); }
.pic__tool { display: flex; align-items: center; gap: 8px; }
.pic__ico { width: 28px; height: 28px; border-radius: 9px; background: var(--card); border: 1px solid var(--edge); display: inline-flex; align-items: center; justify-content: center; color: var(--ink3); }
.pic__ico--hot { background: rgba(10,132,255,.14); border-color: var(--blue); color: var(--blue); box-shadow: 0 0 0 3px rgba(10,132,255,.18); }
.pic__ico--mark { padding: 4px; }
.pic__hint { font: 600 12px/1 var(--font); color: var(--blue); }
.pic__menu { display: inline-flex; align-items: center; gap: 9px; min-width: 190px; padding: 8px 10px; border-radius: 10px; background: var(--card); border: 1px solid var(--edge); font: 600 12.5px/1 var(--font); color: var(--ink); }
.pic__pin { margin-left: auto; color: var(--ink3); display: inline-flex; }
.mark--xs { width: 18px; height: 18px; border-radius: 5px; box-shadow: none; }
.mark--xs svg { display: block; }
.pic__popup { width: 200px; padding: 10px; border-radius: 12px; background: var(--card); border: 1px solid var(--edge); display: flex; flex-direction: column; gap: 9px; }
.pic__popuphead { display: flex; align-items: center; gap: 7px; font: 600 11.5px/1.2 var(--font); color: var(--ink3); }
.pic__btn { display: block; height: 32px; border-radius: 16px; background: var(--blue); color: #fff; font: 600 13px/32px var(--font); text-align: center; }
.how__s b { font-weight: 600; color: var(--ink2); }
.how__icon { display: inline-flex; vertical-align: -3px; width: 16px; height: 16px; border-radius: 4px; background: linear-gradient(158deg, #0a84ff, #0a4fd6); margin: 0 2px; }
.how__puzzle { display: inline-flex; align-items: center; justify-content: center; vertical-align: -4px; width: 20px; height: 20px; border-radius: 6px; background: var(--fill); color: var(--ink2); margin: 0 2px; }
.how__puzzle svg { display: block; }

.ghosts { margin-top: 22px; display: flex; flex-direction: column; gap: 8px; }
.ghost { height: 58px; border-radius: 16px; background: var(--fill); animation: pulse 1.3s ease-in-out infinite; }
.scanning { margin-top: 16px; display: flex; align-items: center; gap: 9px; justify-content: center; font: 500 12.5px/1.2 var(--font); color: var(--ink3); }
.spinner { animation: spin 1s linear infinite; }
.listhead { margin-top: 16px; display: flex; align-items: center; gap: 10px; font: 500 12px/1.3 var(--font); color: var(--ink3); }
.listhead span { flex: 1; }
.linkbtn { border: 0; background: transparent; cursor: pointer; font: 600 12px/1.2 var(--font); color: var(--blue); padding: 6px 2px; }
.rows { margin-top: 8px; display: flex; flex-direction: column; gap: 7px; }
.row { display: flex; align-items: center; gap: 12px; width: 100%; padding: 13px 15px; border: 1px solid var(--edge); border-radius: 16px; background: var(--card); cursor: pointer; text-align: left; transition: background .22s ease, border-color .22s ease, transform .2s var(--spring); animation: fadeUp .38s var(--ease) both; }
.row:active { transform: scale(.985); }
.row.is-on { background: var(--sel-bg); border: 1.5px solid var(--blue); padding: 12.5px 14.5px; }
.row__dot { flex: none; width: 10px; height: 10px; border-radius: 3px; }
.row__body { flex: 1; min-width: 0; }
.row__code { display: block; font: 600 14.5px/1.25 var(--font); color: var(--ink); }
.row__name { display: block; margin-top: 2px; font: 400 12px/1.3 var(--font); color: var(--ink3); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.row__why { display: block; margin-top: 3px; font: 400 12px/1.4 var(--font); color: var(--ink3); text-wrap: pretty; }
.row__nick { flex: 0 0 124px; min-width: 0; height: 32px; padding: 0 10px; border: 1px solid var(--edge); border-radius: 10px; background: var(--fill); font: 500 12.5px/1 var(--font); color: var(--ink); outline: none; cursor: text; }
.row__nick:focus { border-color: var(--blue); }
.row__nick::placeholder { color: var(--ink3); }
@media (max-width: 480px) { .row { flex-wrap: wrap; } .row__nick { order: 9; flex: 1 1 100%; margin-left: 22px; } }
.row__box { flex: none; width: 23px; height: 23px; border-radius: 12px; border: 1.6px solid var(--knob-edge); background: transparent; display: flex; align-items: center; justify-content: center; transition: background .2s ease, border-color .2s ease; }
.row.is-on .row__box { background: var(--blue); border-color: var(--blue); }
.row__box svg { opacity: 0; transform: scale(.5); transition: opacity .18s ease, transform .25s var(--spring); }
.row.is-on .row__box svg { opacity: 1; transform: none; }
.empty { margin-top: 18px; padding: 22px 18px; border-radius: 16px; background: var(--card); border: 1px dashed var(--edge); text-align: center; font: 400 13.5px/1.5 var(--font); color: var(--ink3); display: flex; flex-direction: column; gap: 10px; align-items: center; animation: fadeUp .35s var(--ease) both; }

.panel { margin-top: 18px; display: flex; align-items: center; gap: 14px; padding: 14px 16px; border-radius: 16px; background: var(--card); border: 1px solid var(--edge); }
.panel + .panel { margin-top: 8px; }
.panel__label { flex: 1; font: 500 14.5px/1.3 var(--font); color: var(--ink); }
.reveal { overflow: hidden; transition: height .38s var(--ease), opacity .3s ease, margin .38s var(--ease); }
.reveal.is-closed { height: 0 !important; opacity: 0; margin-top: 0; }
.switch { flex: none; width: 51px; height: 31px; border: 0; border-radius: 16px; cursor: pointer; padding: 2px; background: var(--fill); display: flex; justify-content: flex-start; transition: background .25s ease; }
.switch.is-on { background: #34c759; }
.switch__knob { width: 27px; height: 27px; border-radius: 14px; background: #fff; box-shadow: 0 1px 3px rgba(0,0,0,.25); transition: transform .3s var(--spring); }
.switch.is-on .switch__knob { transform: translateX(20px); }
.stepper { flex: none; display: flex; align-items: center; gap: 2px; padding: 2px; border-radius: 11px; background: var(--fill); }
.stepper button { width: 30px; height: 28px; border: 0; border-radius: 9px; background: transparent; cursor: pointer; font: 600 16px/1 var(--font); color: var(--ink2); transition: background .18s ease, transform .18s var(--spring); }
.stepper button:hover { background: var(--card); }
.stepper button:active { transform: scale(.9); }
.stepper__val { min-width: 46px; text-align: center; font: 600 15px/1 var(--mono); color: var(--ink); display: inline-block; }
.stepper__val.is-tick { animation: pop .28s var(--spring) both; }
.kicker { margin-top: 18px; padding: 0 2px 9px; font: 600 11px/1.2 var(--font); color: var(--ink3); text-transform: uppercase; letter-spacing: .06em; }
.target { display: flex; align-items: center; gap: 12px; padding: 11px 15px; border-radius: 16px; background: var(--card); border: 1px solid var(--edge); animation: fadeUp .38s var(--ease) both; }
.target + .target { margin-top: 7px; }
.target__code { flex: 1; min-width: 0; font: 600 14px/1.3 var(--font); color: var(--ink); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.target .seg[hidden] { display: none; }
.target__pfwrap { flex: none; display: flex; align-items: center; gap: 8px; margin-left: 4px; }
.target__pflabel { font: 500 12px/1 var(--font); color: var(--ink3); }
.page--fr .target .switch { width: 40px; height: 24px; border-radius: 12px; }
.page--fr .target .switch__knob { width: 20px; height: 20px; border-radius: 10px; }
.page--fr .target .switch.is-on .switch__knob { transform: translateX(16px); }
.seg { flex: none; display: flex; padding: 2px; border-radius: 11px; background: var(--fill); border: 1px solid var(--edge); gap: 2px; }
.seg button { border: 0; cursor: pointer; min-width: 33px; height: 27px; border-radius: 9px; font: 600 12.5px/1 var(--font); background: transparent; color: var(--ink3); transition: background .2s ease, color .2s ease, transform .2s var(--spring); }
.seg button.is-on { background: var(--blue); color: #fff; box-shadow: 0 1px 3px rgba(10,132,255,.35); }
.seg button:active { transform: scale(.94); }

.provs { margin-top: 18px; display: flex; gap: 8px; }
.prov { flex: 1; min-width: 0; padding: 13px 14px; border: 1px solid var(--edge); border-radius: 16px; background: var(--card); cursor: pointer; text-align: left; transition: background .22s ease, border-color .22s ease, transform .2s var(--spring); animation: fadeUp .38s var(--ease) both; }
.prov:active { transform: scale(.98); }
.prov.is-on { background: var(--sel-bg); border: 1.5px solid var(--blue); padding: 12.5px 13.5px; }
.prov.is-soon { cursor: default; }
.prov.is-soon:active { transform: none; }
.prov__n { display: block; font: 600 14px/1.25 var(--font); color: var(--ink); }
.prov.is-soon .prov__n { color: var(--ink3); }
.prov__s { display: block; margin-top: 2px; font: 400 11.5px/1.3 var(--font); color: var(--ink3); }
.field--key { margin-top: 10px; }
.keysteps { margin-top: 16px; display: flex; flex-direction: column; gap: 8px; }
.keystep { display: flex; gap: 10px; align-items: baseline; font: 400 13px/1.45 var(--font); color: var(--ink2); text-wrap: pretty; animation: fadeUp .38s var(--ease) both; }
.keystep b { flex: none; font: 600 11.5px/1.4 var(--mono); color: var(--ink3); }
.keystep a { color: var(--blue); text-decoration: none; }
.result { margin-top: 10px; min-height: 18px; font: 500 12.5px/1.4 var(--font); color: var(--ink3); transition: color .2s ease; }
.result.is-ok { color: var(--green); }
.result.is-err { color: #ff453a; }

.done { display: flex; flex-direction: column; align-items: center; text-align: center; gap: 12px; padding: 14px 0 2px; }
.done__check { position: relative; width: 62px; height: 62px; border-radius: 31px; background: rgba(52,199,89,.16); display: flex; align-items: center; justify-content: center; animation: pop .55s var(--spring) both; }
.done__check::before { content: ""; position: absolute; inset: 0; border-radius: 50%; border: 2px solid rgba(52,199,89,.5); animation: ripple .9s ease-out .25s both; }
.done__check .arc { stroke-dashoffset: var(--len); animation: drawStroke .5s var(--ease) .2s both; }
.done .h1 { margin-top: 4px; font-size: 28px; line-height: 1.15; letter-spacing: -.035em; animation: fadeUp .45s var(--ease) .2s both; }
.done .sub { margin: 0; max-width: 300px; font-size: 14px; animation: fadeUp .45s var(--ease) .3s both; }
.summary { margin-top: 20px; display: flex; flex-direction: column; gap: 1px; }
.summary__row { display: flex; align-items: center; gap: 12px; padding: 11px 2px; border-top: 1px solid var(--sep); animation: fadeUp .4s var(--ease) both; }
.summary__k { flex: 1; min-width: 0; font: 400 13px/1.35 var(--font); color: var(--ink3); }
.summary__v { flex: none; font: 600 13px/1.35 var(--font); color: var(--ink); max-width: 250px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.summary__v.is-on { color: var(--green); }
.summary__v.is-off { color: var(--ink3); }

@media (max-width: 480px) {
  .page { padding: 20px 14px 40px; }
  .card { padding: 24px 20px 20px; border-radius: 22px; }
  .provs { flex-direction: column; }
  .seg button { min-width: 30px; }
}

/* ==== the guided setup over the Canvas page (the "First-Run Setup" mockup) ====================
 * An opaque ground, a word-mark first, then a rail of steps down the left and the step itself on
 * the right. Its palette is its own (flat, no glass), so the values are set on the overlay and the
 * rules below are scoped to .page--fr where they re-dress a class the install page also uses. */
@keyframes frBandIn { 0% { opacity: 0; transform: translateX(-46px); } 100% { opacity: 1; transform: none; } }
@keyframes frDotPop { 0% { opacity: 0; transform: scale(.2); } 70% { opacity: 1; transform: scale(1.25); } 100% { opacity: 1; transform: scale(1); } }
@keyframes frVeilOut { 0% { opacity: 1; } 100% { opacity: 0; } }
@keyframes frRise { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: none; } }
.overlay.overlay--solid, html[data-theme="light"] .overlay--solid {
  --bg: #fbfbfd; --tile: #f4f4f7; --panel: #fff; --ink: #111113; --ink2: #3c3c43; --ink3: #6c6c70; --ink4: #77777c;
  --hair: rgba(60,60,67,.12); --faint: rgba(60,60,67,.18); --stage: rgba(60,60,67,.06); --box-edge: rgba(60,60,67,.3);
  --rail-on: rgba(60,60,67,.06); --row-on: rgba(10,132,255,.09); --off: rgba(118,118,128,.16); --switch-off: rgba(118,118,128,.2);
  --track: rgba(60,60,67,.08); --thumb: rgba(60,60,67,.32); --thumb-hot: rgba(60,60,67,.5);
  background: var(--bg); -webkit-backdrop-filter: none; backdrop-filter: none; overflow-y: auto; color: var(--ink);
}
:host([data-theme="dark"]) .overlay.overlay--solid, html[data-theme="dark"] .overlay--solid {
  --bg: #000; --tile: #121214; --panel: #1e1e21; --ink: #f5f5f7; --ink2: #d1d1d6; --ink3: #98989d; --ink4: #9b9ba1;
  --hair: rgba(255,255,255,.1); --faint: rgba(255,255,255,.16); --stage: rgba(255,255,255,.05); --box-edge: rgba(255,255,255,.3);
  --rail-on: rgba(255,255,255,.07); --row-on: rgba(10,132,255,.16); --off: rgba(118,118,128,.24); --switch-off: rgba(118,118,128,.32);
  --track: rgba(255,255,255,.08); --thumb: rgba(255,255,255,.32); --thumb-hot: rgba(255,255,255,.5);
  background: var(--bg);
}
.overlay .page--fr { height: auto; min-height: 100%; overflow: visible; padding: 40px 32px; align-items: center; justify-content: center; gap: 0; }

/* the word-mark: three bands of "Simpl" slide in, the dot pops, then the veil goes */
.intro { position: fixed; inset: 0; z-index: 6; display: flex; align-items: center; justify-content: center; background: var(--bg); }
.intro[hidden] { display: none; }
.intro.is-fading { animation: frVeilOut .4s ease both; }
.intro__svg { display: block; overflow: visible; }
.intro__word { font: 600 116px/1 var(--display); letter-spacing: -5px; }
.intro__word--1 { fill: var(--ink); }
.intro__word--2 { fill: var(--ink3); }
.intro__word--3 { fill: var(--ink4); }
.intro__band { animation: frBandIn .58s var(--ease) both; }
.intro__band--b { animation-delay: 140ms; }
.intro__band--c { animation-delay: 280ms; }
.intro__dot { fill: var(--blue); transform-origin: 288px 90px; animation: frDotPop .42s var(--ease) 800ms both; }

/* the body: a top line, then the rail and the step */
.fr { width: 100%; max-width: 1080px; display: flex; flex-direction: column; gap: 26px; opacity: 0; }
.fr.is-in { opacity: 1; animation: frRise .44s var(--ease) both; }
.fr__top { display: flex; align-items: center; gap: 11px; }
.fr__brand { flex: none; display: flex; align-items: center; gap: 11px; border: 0; background: transparent; padding: 0; cursor: pointer; font: 500 13.5px/1.2 var(--font); letter-spacing: -.012em; color: var(--ink3); }
.fr__brand svg { display: block; flex: none; }
.fr__arc1 { stroke: var(--ink); }
.fr__arc2 { stroke: var(--ink4); }
.fr__spacer { flex: 1; }
.fr__count { flex: none; font: 500 12px/1.2 var(--font); color: var(--ink4); }
.fr__cols { display: flex; align-items: stretch; min-height: 560px; }
.rail { flex: none; width: 250px; padding-right: 28px; border-right: 1px solid var(--hair); display: flex; flex-direction: column; gap: 3px; }
.rail__item { display: flex; align-items: flex-start; gap: 12px; width: 100%; padding: 13px 12px; border: 0; border-radius: 11px; background: transparent; cursor: pointer; text-align: left; color: inherit; opacity: 1; transition: background .2s ease; }
.rail__item.is-active { background: var(--rail-on); }
.rail__item.is-locked, .rail__item:disabled { cursor: default; }
.rail__mark { flex: none; width: 19px; height: 19px; border-radius: 10px; margin-top: 1px; border: 1px solid var(--hair); display: flex; align-items: center; justify-content: center; font: 600 10px/1 var(--mono); color: var(--ink4); transition: background .2s ease, border-color .2s ease; }
.rail__item.is-active .rail__mark { border-color: var(--blue); color: var(--ink); }
.rail__item.is-done .rail__mark { background: var(--blue); border-color: var(--blue); }
.rail__mark svg { display: block; }
.rail__body { flex: 1; min-width: 0; }
.rail__name { display: block; font: 400 14.5px/1.3 var(--font); letter-spacing: -.012em; color: var(--ink2); transition: color .2s ease; }
.rail__item.is-active .rail__name { font-weight: 600; color: var(--ink); }
.rail__item.is-locked .rail__name { color: var(--ink4); }
.rail__answer { display: block; margin-top: 3px; font: 400 12.5px/1.3 var(--font); color: var(--ink4); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.rail__item.is-active .rail__answer { color: var(--ink3); }
.fr__main { flex: 1; min-width: 0; padding-left: 36px; display: flex; flex-direction: column; }
.fr__body { flex: 1; min-height: 0; transition: opacity .15s ease, transform .15s ease; }
.fr__body.is-leaving { opacity: 0; transform: translateX(var(--leave, -14px)); }
.fr__body.is-rising { animation: frRise .3s var(--ease) both; }
.fr__h1 { margin: 0; font: 600 36px/1.15 var(--display); letter-spacing: -.03em; color: var(--ink); text-wrap: pretty; }
.fr__blurb { margin: 10px 0 0; max-width: 560px; font: 400 17px/1.5 var(--font); color: var(--ink3); text-wrap: pretty; }
.fr__blurb--strong { max-width: 600px; font: 700 19px/1.4 var(--font); color: var(--blue); } /* the courses step's one instruction */
.fr__foot { flex: none; padding-top: 26px; display: flex; align-items: center; gap: 18px; }
.fr__hint { flex: 1; min-width: 0; font: 400 11.5px/1.4 var(--font); color: var(--ink4); text-wrap: pretty; }
.page--fr .btn { height: 42px; border-radius: 21px; padding: 0 22px; font: 600 15px/1 var(--display); letter-spacing: -.012em; transition: background .22s ease, color .22s ease, box-shadow .22s ease; }
.page--fr .btn:hover { transform: none; }
.page--fr .fr__next { display: flex; align-items: center; gap: 8px; box-shadow: 0 2px 10px rgba(10,132,255,.32); }
.page--fr .fr__next:disabled { background: var(--off); color: var(--ink4); box-shadow: none; }
.page--fr .fr__next svg { display: block; }
.page--fr .fr__back { border: 1px solid var(--hair); padding: 0 20px; font: 600 14.5px/1 var(--font); color: var(--ink2); transition: background .2s ease, border-color .2s ease; }
.page--fr .fr__back:hover { background: var(--tile); }
.mscroll { overflow-y: auto; overscroll-behavior: contain; }
.mscroll::-webkit-scrollbar { width: 0; height: 0; }

/* 1 · the courses */
.page--fr .listhead { margin-top: 22px; align-items: baseline; gap: 12px; font: 500 11.5px/1.2 var(--font); color: var(--ink4); }
.page--fr .linkbtn { flex: none; height: 28px; padding: 0 12px; border: 1px solid var(--hair); border-radius: 14px; background: transparent; font: 600 12px/1 var(--font); color: var(--blue); transition: background .2s ease; }
.page--fr .linkbtn:hover { background: var(--tile); }
/* the course list scrolls behind a scrollbar that is always drawn (a classic one, not the overlay kind
 * that hides between scrolls), so it is plain that there are more courses below */
.page--fr .rows { margin-top: 4px; max-height: 380px; gap: 2px; overflow-y: scroll; padding-right: 8px; scrollbar-width: thin; scrollbar-color: var(--thumb) var(--track); }
.page--fr .rows::-webkit-scrollbar { width: 8px; height: auto; }
.page--fr .rows::-webkit-scrollbar-track { background: var(--track); border-radius: 4px; }
.page--fr .rows::-webkit-scrollbar-thumb { background: var(--thumb); border-radius: 4px; border: 0; }
.page--fr .rows::-webkit-scrollbar-thumb:hover { background: var(--thumb-hot); }
.page--fr .rows::-webkit-scrollbar-button { display: none; width: 0; height: 0; }
.page--fr .row { gap: 14px; padding: 13px 12px 13px 11px; border: 0; border-radius: 11px; background: transparent; animation: frRise .32s var(--ease) both; transition: background .2s ease; }
.page--fr .row:active { transform: none; }
.page--fr .row.is-on { background: var(--row-on); border: 0; padding: 13px 12px 13px 11px; }
.page--fr .row__box { width: 20px; height: 20px; border-radius: 6px; border: 1.5px solid var(--box-edge); transition: background .18s ease, border-color .18s ease; }
.page--fr .row__box svg { transform: none; transition: opacity .16s ease; }
.page--fr .row__dot { width: 6px; height: 6px; border-radius: 3px; opacity: .3; transition: opacity .2s ease; }
.page--fr .row.is-on .row__dot { opacity: 1; }
.page--fr .row__code { font: 400 15.5px/1.3 var(--font); letter-spacing: -.012em; color: var(--ink3); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; transition: color .2s ease; }
.page--fr .row.is-on .row__code { font-weight: 600; color: var(--ink); }
.page--fr .row__name { margin-top: 2px; font: 400 12px/1.3 var(--font); color: var(--ink4); }
.page--fr .row__nick { flex: none; width: 92px; height: auto; border: 0; border-bottom: 1px solid var(--hair); border-radius: 0; background: transparent; padding: 0 0 3px; font: 400 12.5px/1.3 var(--font); color: var(--ink2); text-align: right; }
.page--fr .row__nick:focus { border-color: var(--blue); }
.page--fr .row:not(.is-on) .row__nick { display: none; }
.page--fr .ghosts { margin-top: 22px; gap: 2px; }
.scrollhint { margin: 10px 2px 0; font: 700 13.5px/1.3 var(--font); color: #b42318; animation: frRise .32s var(--ease) both; }
:host([data-theme="dark"]) .scrollhint, html[data-theme="dark"] .scrollhint { color: #f97066; }
.page--fr .ghost { height: 52px; border-radius: 11px; background: var(--tile); }
.page--fr .scanning { color: var(--ink4); }
.page--fr .empty { background: var(--tile); border-color: var(--hair); }

/* 2 · grades */
.fr__lines { margin-top: 22px; }
.fr__line { display: flex; align-items: center; gap: 16px; padding: 17px 0; border-top: 1px solid var(--hair); animation: frRise .28s var(--ease) both; }
.fr__line.is-hidden { display: none; }
.fr__linebody { flex: 1; min-width: 0; }
.fr__linet { display: block; font: 500 15.5px/1.3 var(--font); letter-spacing: -.012em; color: var(--ink); }
.fr__lines2 { display: block; margin-top: 3px; font: 400 11.5px/1.35 var(--font); color: var(--ink4); }
.page--fr .switch { width: 46px; height: 28px; border-radius: 14px; padding: 2px; background: var(--switch-off); }
.page--fr .switch.is-on { background: #34c759; }
.page--fr .switch__knob { width: 24px; height: 24px; border-radius: 12px; box-shadow: none; }
.page--fr .switch.is-on .switch__knob { transform: translateX(18px); }
.page--fr .stepper { gap: 13px; padding: 0; border-radius: 0; background: transparent; }
.page--fr .stepper button { width: 20px; height: 20px; border-radius: 10px; font: 400 18px/1 var(--font); color: var(--ink3); }
.page--fr .stepper button:hover { background: var(--tile); }
.page--fr .stepper__val { min-width: 42px; font: 500 15.5px/1 var(--mono); }
.page--fr .kicker { margin: 0; padding: 22px 0 2px; font: 500 11.5px/1.2 var(--font); color: var(--ink4); text-transform: none; letter-spacing: 0; }
.page--fr .targets { max-height: 260px; }
.page--fr .target { gap: 13px; padding: 12px 0; border: 0; border-top: 1px solid var(--hair); border-radius: 0; background: transparent; animation: frRise .32s var(--ease) both; }
.page--fr .target + .target { margin-top: 0; }
.page--fr .target .row__dot { opacity: 1; }
.page--fr .target__code { font: 500 14px/1.3 var(--font); letter-spacing: -.012em; }
.page--fr .seg { gap: 1px; padding: 0; border: 0; border-radius: 0; background: transparent; }
.page--fr .seg button { min-width: 30px; height: 26px; border-radius: 8px; font: 400 12.5px/1 var(--font); color: var(--ink4); }
.page--fr .seg button.is-on { background: var(--blue); color: #fff; font-weight: 600; box-shadow: none; }

/* 3 · 4 · the preview tiles: miniatures of the real layouts in the student's own course colours */
.tiles { margin-top: 24px; display: flex; gap: 12px; }
.tiles--2 { gap: 16px; max-width: 640px; }
.tile { flex: 1; min-width: 0; padding: 0; border: 0; background: transparent; cursor: pointer; text-align: left; display: flex; flex-direction: column; gap: 11px; color: inherit; font: inherit; }
.tile__frame { display: block; height: 136px; padding: 12px; border: 1px solid var(--hair); border-radius: 13px; background: var(--tile); transition: border-color .2s ease, box-shadow .2s ease; }
.tile[data-view="cards"] .tile__frame, .tiles--2 .tile__frame { padding: 10px; }
.tiles--2 .tile__frame { height: 164px; }
.tile.is-on .tile__frame { border-color: var(--blue); box-shadow: inset 0 0 0 1px var(--blue); }
.tile__foot { display: flex; align-items: flex-start; gap: 8px; }
.tile__body { flex: 1; min-width: 0; }
.tile__t { display: block; font: 400 15.5px/1.3 var(--font); color: var(--ink3); transition: color .2s ease; }
.tile.is-on .tile__t { font-weight: 600; color: var(--ink); }
.tile__s { display: block; margin-top: 3px; font: 400 12.5px/1.4 var(--font); color: var(--ink4); text-wrap: pretty; }
.tile__state { flex: none; display: flex; align-items: center; gap: 6px; margin-top: 1px; }
.tile__label { font: 600 10.5px/1.2 var(--font); letter-spacing: .02em; color: var(--ink4); transition: color .2s ease; }
.tile.is-on .tile__label { color: var(--blue); }
.tile__badge { width: 18px; height: 18px; border-radius: 9px; border: 1.5px solid var(--box-edge); display: flex; align-items: center; justify-content: center; transition: background .18s ease, border-color .18s ease; }
.tile.is-on .tile__badge { background: var(--blue); border-color: var(--blue); }
.tile__badge svg { display: block; opacity: 0; transition: opacity .16s ease; }
.tile.is-on .tile__badge svg { opacity: 1; }
.mini { display: flex; height: 100%; }
.mini--grid { display: grid; grid-template-columns: 1fr 1fr; grid-template-rows: 1fr 1fr; gap: 7px; }
.mini__card { display: block; border-radius: 6px; }
.mini--col { flex-direction: column; gap: 9px; justify-content: center; }
.mini--feed { gap: 11px; }
.mini__row { display: flex; align-items: center; gap: 7px; }
.mini--feed .mini__row { gap: 8px; }
.mini__row--tight { gap: 4px; }
.mini__dot { flex: none; display: block; border-radius: 50%; }
.mini__bar { display: block; flex: 1; min-width: 0; height: 5px; border-radius: 3px; }
.mini__stack { flex: 1; min-width: 0; display: block; }
.mini__bar--faint { background: var(--faint); }
.mini--feed .mini__bar--faint { margin-top: 4px; height: 4px; }
.mini__bar--accent { background: var(--blue); }
.mini--side { gap: 9px; position: relative; }
.mini__nav { flex: none; width: 58px; display: flex; flex-direction: column; gap: 5px; }
.mini__nav .mini__bar--faint { flex: none; width: 100%; }
.mini__hair { display: block; margin-top: 5px; height: 1px; background: var(--hair); }
.mini__stage { flex: 1; display: block; border-radius: 7px; background: var(--stage); }
.mini__flyout { position: absolute; left: 62px; top: 14px; width: 64px; padding: 6px; border-radius: 7px; background: var(--panel); border: 1px solid var(--hair); display: flex; flex-direction: column; gap: 4px; }
/* the look step: a light window, a dark one, and one split down the middle for Automatic */
.mini--look { flex-direction: column; gap: 7px; padding: 9px; box-sizing: border-box; border-radius: 8px; border: 1px solid rgba(60,60,67,.14); }
.mini--look-light { background: #fff; }
.mini--look-dark { background: #1c1c1e; border-color: rgba(255,255,255,.12); }
.mini--look-system { flex-direction: row; padding: 0; overflow: hidden; }
.mini__half { flex: 1; display: flex; flex-direction: column; gap: 7px; padding: 9px; }
.mini__half--light { background: #fff; }
.mini__half--dark { background: #1c1c1e; }
.mini__lookbar { display: block; height: 7px; width: 42%; border-radius: 4px; background: #1c1c1e; }
.mini__lookrow { display: block; height: 5px; width: 100%; border-radius: 3px; background: rgba(60,60,67,.18); }
.mini__lookrow--short { width: 62%; }
.mini__lookbar.mini__lookrow--dark { background: #f2f2f7; }
.mini__lookrow.mini__lookrow--dark { background: rgba(255,255,255,.22); }

/* ==== Personalize (content/app/personalize.js): the look, a colour, the courses' colours, photos,
 * on a preview of the Dashboard — in the setup card's place, so the overlay is its ground ==== */
@keyframes omRise { from { opacity: 0; transform: translateY(12px); } to { opacity: 1; transform: none; } }
@keyframes omFade { from { opacity: 0; } to { opacity: 1; } }
@keyframes omMorphIn { from { opacity: .4; transform: translate(-50%, -50%) scale(.13); } to { opacity: 1; transform: translate(-50%, -50%) scale(1); } }
@keyframes omMorphOut { from { opacity: 1; transform: translate(-50%, -50%) scale(1); } to { opacity: 0; transform: translate(-50%, -50%) scale(.13); } }
@keyframes omPkDraw { from { stroke-dashoffset: 1; } to { stroke-dashoffset: 0; } }
@keyframes omPkKnob { from { opacity: 0; transform: scale(0); } 60% { opacity: 1; transform: scale(1.18); } to { opacity: 1; transform: scale(1); } }
@keyframes omPkSpin { from { opacity: 0; transform: rotate(-70deg) scale(.92); } to { opacity: 1; transform: rotate(0) scale(1); } }
@keyframes omPkCore { from { opacity: 0; transform: scale(.55); } to { opacity: 1; transform: scale(1); } }
@keyframes omPkLbl { from { opacity: 0; } to { opacity: 1; } }
@keyframes omPop { from { opacity: 0; transform: translate(-50%, 10px) scale(.97); } to { opacity: 1; transform: translate(-50%, 0) scale(1); } }
.overlay .page--fr:has(.pz) { padding: 14px 28px 0; justify-content: flex-start; background: #f2f2f6; transition: background .3s ease; }
:host([data-theme="dark"]) .overlay .page--fr:has(.pz), html[data-theme="dark"] .overlay .page--fr:has(.pz) { background: #000; }
.pz { --pz-bg: #f2f2f6; --pz-tile: #e6e6eb; --pz-field: #fff; --pz-glass: rgba(255,255,255,.82); --pz-ink: #1c1c1e; --pz-ink2: #3c3c43; --pz-ink3: #6c6c70; --pz-hair: rgba(60,60,67,.12); --pz-hair2: #c7c7cc; --pz-seg-on: #fff;
  --pv-main: #fbfbfd; --pv-side: #f0f0f4; --pv-card: #fff; --pv-head: #f0f0f4; --pv-ink: #1c1c1e; --pv-hair: rgba(60,60,67,.08); --pv-shadow: .12;
  width: 100%; max-width: 980px; display: flex; flex-direction: column; animation: omFade .3s ease both; font-family: var(--font); color: var(--pz-ink); }
:host([data-theme="dark"]) .pz, html[data-theme="dark"] .pz { --pz-bg: #000; --pz-tile: #161618; --pz-field: #2a2a2d; --pz-glass: rgba(30,30,32,.78); --pz-ink: #f5f5f7; --pz-ink2: #c7c7cc; --pz-ink3: #8e8e93; --pz-hair: rgba(255,255,255,.09); --pz-hair2: #48484a; --pz-seg-on: #2c2c2e;
  --pv-main: #0b0b0d; --pv-side: #151517; --pv-card: #1c1c1e; --pv-head: #111113; --pv-ink: #f2f2f7; --pv-hair: rgba(255,255,255,.06); --pv-shadow: .5; }
.pz button { font-family: inherit; }
/* the top row: the brand, the bars, the appearance */
.pz__top { display: flex; align-items: center; gap: 10px; }
.pz__brand { flex: 1; min-width: 0; display: flex; align-items: center; gap: 10px; }
.pz__tile { flex: none; width: 24px; height: 24px; border-radius: 7px; background: var(--A-btn); display: flex; align-items: center; justify-content: center; transition: background .3s ease; }
.pz__tile svg { display: block; }
.pz__name { font: 600 13px/1.2 var(--font); color: var(--pz-ink2); }
.pz__bars { flex: none; display: flex; align-items: center; gap: 6px; }
.pz__bar { width: 14px; height: 5px; border: 0; padding: 0; border-radius: 3px; cursor: pointer; background: var(--pz-hair); transition: width .35s var(--ease), background .3s ease; }
.pz__bar.is-done { background: var(--A-btn); }
.pz__bar.is-on { width: 36px; }
.pz__seg { flex: 1; min-width: 0; display: flex; justify-content: flex-end; }
.pz__seg { display: flex; padding: 2px; border-radius: 10px; background: var(--pz-tile); gap: 2px; margin-left: auto; flex: none; }
.pz__segbtn { display: flex; align-items: center; gap: 6px; height: 28px; padding: 0 10px; border: 0; border-radius: 8px; cursor: pointer; background: transparent; font: 500 12px/1 var(--font); color: var(--pz-ink3); transition: background .2s ease; }
.pz__segbtn.is-on { background: var(--pz-seg-on); box-shadow: 0 1px 3px rgba(0,0,0,.18); color: var(--pz-ink); font-weight: 600; }
.pz__lookpv { position: relative; flex: none; width: 18px; height: 12px; border-radius: 3px; overflow: hidden; box-shadow: inset 0 0 0 1px var(--pz-hair); }
.pz__lookpv > b { position: absolute; left: 2px; top: 2px; bottom: 2px; width: 4px; border-radius: 1px; }
.pz__lookpv--light { background: #f2f2f6; } .pz__lookpv--light > b { background: #e3e3e8; }
.pz__lookpv--dark { background: #000; } .pz__lookpv--dark > b { background: #1c1c1e; }
.pz__lookpv--system { background: linear-gradient(110deg, #f2f2f6 50%, #000 50%); } .pz__lookpv--system > b { background: rgba(128,128,134,.4); }
/* the stage: the heading, the preview, the controls */
.pz__stage { min-height: 480px; display: flex; flex-direction: column; align-items: center; padding-top: 14px; }
.pz__head { width: 100%; display: flex; flex-direction: column; align-items: center; animation: omRise .36s var(--ease) both; }
.pz__h1 { margin: 0; font: 700 24px/1.15 var(--display); letter-spacing: -.03em; color: var(--pz-ink); text-align: center; }
.pz__lead { margin: 5px 0 0; font: 400 14px/1.45 var(--font); color: var(--pz-ink3); text-align: center; }
.pz__pvwrap { position: relative; width: 100%; height: min(430px, 46vw, max(170px, calc(100vh - 500px))); margin-top: 14px; overflow: hidden; } /* (short windows: the preview gives way, so the foot stays in view) */
@media (max-height: 760px) { .pz__h1 { font-size: 19px; } .pz__pvwrap { margin-top: 8px; } .pz__foot { margin-top: 6px; padding: 6px 0 8px; } }
.pz__pv { position: absolute; left: 50%; top: 0; width: 980px; height: 430px; transform: translateX(-50%); transform-origin: top center; border-radius: 22px; overflow: hidden; border: 1px solid var(--pz-hair); background: var(--pv-main); display: flex; box-shadow: 0 30px 70px rgba(0,0,0,var(--pv-shadow)); }
.pz__side { position: relative; flex: none; width: 190px; overflow: hidden; background: var(--pv-side); border-radius: 22px 0 0 22px; outline: 2px solid transparent; outline-offset: -2px; transition: outline-color .2s ease; }
.pz__side.is-pickable { cursor: pointer; }
.pz__side.is-pickable:hover { outline: 2px dashed var(--hover-ring); }
.pz__side.is-target { outline: 2px solid var(--A) !important; }
.pz__pic { position: absolute; inset: 0; display: block; pointer-events: none; }
.pz__pic--sharp { background: var(--pic) center / cover no-repeat; animation: omFade .35s ease both; }
.pz__pic--blur { inset: -24px; background: var(--pic) center / cover no-repeat; filter: blur(16px); }
.pz__side .pz__pic--blur { -webkit-mask-image: linear-gradient(to top, transparent 10%, #000 62%); mask-image: linear-gradient(to top, transparent 10%, #000 62%); }
/* inked, as the page draws it (app.css): the paper in the complement, the ink's mask in the colour, the blurred mask under the fade */
.pz { --pz-ink-lift: #000000; --pz-ink-k: 93%; }
:host([data-theme="dark"]) .pz, html[data-theme="dark"] .pz { --pz-ink-lift: #ffffff; --pz-ink-k: 90%; }
.pz__side { --paper: var(--pv-side); } .pz__card { --paper: var(--pv-card); } .pz__hcard { --paper: var(--pv-head); }
.pz__pic--paper { background: var(--paper); }
.pz__pic--sharp.pz__pic--inked { background: color-mix(in srgb, var(--paper) var(--pz-ink-k), var(--pz-ink-lift)); -webkit-mask: var(--ink) center / cover no-repeat; mask: var(--ink) center / cover no-repeat; }
.pz__pic--blur.pz__pic--inked { inset: 0; filter: none; background: color-mix(in srgb, var(--paper) var(--pz-ink-k), var(--pz-ink-lift)); -webkit-mask-size: cover, auto; -webkit-mask-position: center, 0 0; -webkit-mask-repeat: no-repeat; -webkit-mask-composite: source-in; mask-size: cover, auto; mask-position: center, 0 0; mask-repeat: no-repeat; mask-composite: intersect; }
.pz__side .pz__pic--blur.pz__pic--inked { -webkit-mask-image: var(--ink), linear-gradient(to top, transparent 10%, #000 62%); mask-image: var(--ink), linear-gradient(to top, transparent 10%, #000 62%); }
.pz__card .pz__pic--blur.pz__pic--inked { -webkit-mask-image: var(--ink), radial-gradient(150% 150% at 100% 100%, transparent 30%, #000 70%); mask-image: var(--ink), radial-gradient(150% 150% at 100% 100%, transparent 30%, #000 70%); }
.pz__hcard .pz__pic--blur.pz__pic--inked { -webkit-mask-image: var(--ink), linear-gradient(to left, transparent 8%, #000 60%); mask-image: var(--ink), linear-gradient(to left, transparent 8%, #000 60%); }
.pz__choicepic--inked { background: var(--pz-field) !important; position: relative; overflow: hidden; }
.pz__choicepic--inked::after { content: ""; position: absolute; inset: 0; background: color-mix(in srgb, var(--pz-field) calc(var(--pz-ink-k) - 4%), var(--pz-ink-lift)); -webkit-mask: var(--ink) center / cover no-repeat; mask: var(--ink) center / cover no-repeat; }
.pz__thumb i.is-inked { background: var(--pz-field); position: relative; }
.pz__thumb i.is-inked::after { content: ""; position: absolute; inset: 0; border-radius: inherit; background: color-mix(in srgb, var(--pz-field) calc(var(--pz-ink-k) - 4%), var(--pz-ink-lift)); -webkit-mask: var(--ink) center / cover no-repeat; mask: var(--ink) center / cover no-repeat; }
.pz { --pz-wash: rgba(255,255,255,.3); --pz-photo-ink: #1c1c1e; --pz-photo-ink2: rgba(28,28,30,.66); --pz-photo-on: rgba(0,0,0,.08); --pz-photo-chip: rgba(0,0,0,.08); --pz-veil-k: 26%; --pz-edge: rgba(255,255,255,.2); --pz-icon-k: 100%; } /* the photo surfaces as the page has them (app.css --bcv-photo-*): washed heavily in the look's ground, the look's own ink over them */
:host([data-theme="dark"]) .pz, html[data-theme="dark"] .pz { --pz-wash: rgba(0,0,0,.3); --pz-photo-ink: #ffffff; --pz-photo-ink2: rgba(255,255,255,.72); --pz-photo-on: rgba(255,255,255,.18); --pz-photo-chip: rgba(255,255,255,.22); --pz-veil-k: 34%; --pz-edge: rgba(0,0,0,.3); --pz-icon-k: 45%; }
.pz__pic--veil-side { background: linear-gradient(to top, var(--pz-edge) 0%, color-mix(in srgb, var(--veil-side) var(--pz-veil-k), transparent) 70%), var(--pz-wash); }
.pz__pic--inked ~ .pz__pic--veil { background: none; } /* (tone on tone: no veil, no wash) */
.pz__card .pz__pic--blur { inset: -20px; filter: blur(14px); -webkit-mask-image: radial-gradient(150% 150% at 100% 100%, transparent 30%, #000 70%); mask-image: radial-gradient(150% 150% at 100% 100%, transparent 30%, #000 70%); }
.pz__pic--veil-card { background: radial-gradient(150% 150% at 100% 100%, transparent 26%, color-mix(in srgb, var(--veil-card) var(--pz-veil-k), transparent) 70%), var(--pz-wash); }
.pz__hcard .pz__pic--blur { inset: -24px; filter: blur(18px); -webkit-mask-image: linear-gradient(to left, transparent 8%, #000 60%); mask-image: linear-gradient(to left, transparent 8%, #000 60%); }
.pz__pic--veil-head { background: linear-gradient(to left, transparent 0%, color-mix(in srgb, var(--veil-head) var(--pz-veil-k), transparent) 72%), var(--pz-wash); }
.pz__sidein { position: relative; padding: 18px 11px; display: flex; flex-direction: column; gap: 2px; }
.pz__pvbrand { display: flex; align-items: center; gap: 8px; padding: 0 8px 16px; font: 600 13px/1 var(--font); color: var(--pv-ink); }
.pz__pvtile { display: block; width: 18px; height: 18px; border-radius: 5px; background: var(--A-btn); transition: background .3s ease; }
.pz__row { display: flex; align-items: center; gap: 10px; padding: 7px 9px; border-radius: 9px; font: 500 12.5px/1.1 var(--font); color: var(--pv-ink); transition: background .3s ease, color .3s ease; }
.pz__row.is-on { background: var(--A-tint); color: var(--A-read); font-weight: 600; }
.pz__rowic { flex: none; stroke: var(--row); transition: stroke .3s ease; }
.has-pic .pz__pvbrand, .has-pic .pz__row, .has-pic .pz__row.is-on, .has-pic .pz__crow { color: var(--pz-photo-ink); }
.has-pic .pz__row.is-on { background: var(--pz-photo-on); }
.has-pic .pz__rowic { stroke: color-mix(in srgb, var(--row) var(--pz-icon-k), #fff); }
.pz__courselabel { padding: 18px 9px 7px; font: 600 9.5px/1 var(--font); letter-spacing: .07em; color: var(--pz-ink3); }
.has-pic .pz__courselabel { color: var(--pz-photo-ink2); }
.pz__crow { display: flex; align-items: center; gap: 10px; padding: 6px 9px; border-radius: 8px; font: 500 12px/1.1 var(--font); color: var(--pv-ink); transition: background .3s ease; }
.pz__crow span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pz__crow.is-on { background: color-mix(in srgb, var(--c) 14%, transparent); font-weight: 600; }
.has-pic .pz__crow.is-on { background: var(--pz-photo-on); }
.pz__cdot { flex: none; display: block; width: 8px; height: 8px; border-radius: 4px; background: var(--c); transition: background .3s ease; }
.pz__badge { position: absolute; right: 12px; top: 16px; z-index: 2; display: flex; align-items: center; gap: 5px; height: 24px; padding: 0 9px 0 7px; border-radius: 15px; background: var(--pz-field); -webkit-backdrop-filter: blur(10px); backdrop-filter: blur(10px); font: 600 9.5px/1 var(--font); color: var(--pz-ink2); pointer-events: none; animation: omFade .3s ease both; }
.pz__badge svg { display: block; }
.pz__badge.has-pic { background: rgba(0,0,0,.34); color: #fff; }
.pz__badge.is-on { background: var(--A-btn); color: #fff; }
.pz__card .pz__badge { right: 8px; top: auto; bottom: 8px; height: 22px; padding: 0 8px 0 6px; }
.pz__main { flex: 1; min-width: 0; padding: 22px 22px 18px; display: flex; flex-direction: column; gap: 15px; }
.pz__date { display: block; font: 500 11px/1.2 var(--font); color: var(--pz-ink3); }
.pz__title { display: block; margin-top: 3px; font: 700 22px/1.1 var(--display); letter-spacing: -.03em; color: var(--pv-ink); }
.pz__cards { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 10px; }
.pz__card { position: relative; height: 86px; border-radius: 14px; overflow: hidden; background: var(--pv-card); outline: 2px solid transparent; outline-offset: 2px; transition: outline-color .2s ease; }
.pz__card.is-pickable { cursor: pointer; }
.pz__card.is-pickable:hover { outline: 2px dashed var(--hover-ring); }
.pz__card.is-target { outline: 2px solid var(--A) !important; }
.pz__cardin { position: relative; height: 100%; box-sizing: border-box; padding: 12px 13px; display: flex; flex-direction: column; }
.pz__chead { display: flex; align-items: flex-start; gap: 6px; }
.pz__cic { flex: none; margin-top: 1px; stroke: var(--ic); transition: stroke .3s ease; }
.pz__clabel { flex: 1; min-width: 0; font: 600 10.5px/1.25 var(--font); color: var(--pz-ink2); }
.pz__cn { flex: none; font: 700 24px/1 var(--display); letter-spacing: -.03em; color: var(--A-read); transition: color .3s ease; }
.pz__cnote { margin-top: auto; font: 400 10px/1.2 var(--font); color: var(--pz-ink3); }
.pz__card.has-pic .pz__cic { stroke: var(--pz-photo-ink); } .pz__card.has-pic .pz__clabel { color: var(--pz-photo-ink); } .pz__card.has-pic .pz__cn { color: var(--pz-photo-ink); } .pz__card.has-pic .pz__cnote { color: var(--pz-photo-ink2); }
.pz__list { border-radius: 14px; background: var(--pv-card); overflow: hidden; }
.pz__lrow { display: flex; align-items: center; gap: 10px; padding: 10px 13px; border-top: 1px solid var(--pv-hair); }
.pz__ring { flex: none; display: block; width: 12px; height: 12px; border-radius: 6px; box-sizing: border-box; border: 1.6px solid var(--A); transition: border-color .3s ease; }
.pz__lt { flex: 1; min-width: 0; font: 500 12px/1.2 var(--font); color: var(--pv-ink); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pz__chip { flex: none; padding: 3px 8px; border-radius: 7px; background: color-mix(in srgb, var(--c) 14%, transparent); font: 600 10.5px/1.2 var(--font); color: var(--c-read, var(--c)); transition: color .3s ease, background .3s ease; }
/* the headers, as cards */
.pz__heads { height: 100%; box-sizing: border-box; overflow-y: auto; padding: 4px; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); grid-auto-rows: 92px; align-content: start; gap: 10px; }
.pz__hcard { position: relative; display: block; width: 100%; height: 92px; padding: 0; border: 0; border-radius: 14px; overflow: hidden; cursor: pointer; text-align: left; background: var(--pv-head); outline: 2px solid transparent; outline-offset: 2px; transition: outline-color .2s ease; animation: omRise .32s var(--ease) both; color: inherit; }
.pz__hcard:hover { outline: 2px dashed var(--hover-ring); }
.pz__hcard.is-on { outline: 2px solid var(--A) !important; }
.pz__hin { position: relative; height: 100%; box-sizing: border-box; padding: 10px 12px 12px 16px; display: flex; align-items: flex-end; justify-content: space-between; gap: 10px; }
.pz__htitle { flex: 1; min-width: 0; font: 700 17px/1.2 var(--display); letter-spacing: -.025em; color: var(--pv-ink); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.pz__hcard.has-pic .pz__htitle { color: var(--pz-photo-ink); }
.pz__hchip { flex: none; align-self: flex-start; display: flex; align-items: center; gap: 6px; height: 24px; padding: 0 10px 0 9px; border-radius: 12px; white-space: nowrap; background: var(--pz-field); -webkit-backdrop-filter: blur(10px); backdrop-filter: blur(10px); font: 600 11.5px/1 var(--font); color: var(--pz-ink2); }
.pz__hchip svg { display: block; }
.pz__hcard.is-on .pz__hchip { background: var(--A-btn); color: #fff; }
.pz__hcard.has-pic .pz__hchip { background: var(--pz-photo-chip); color: var(--pz-photo-ink); }
/* the photo bar, and the photo choices */
.pz__bar2 { position: absolute; left: 50%; bottom: 16px; z-index: 5; display: flex; align-items: center; gap: 12px; padding: 10px 12px 10px 16px; border-radius: 20px; background: var(--pz-glass); -webkit-backdrop-filter: blur(24px) saturate(1.5); backdrop-filter: blur(24px) saturate(1.5); border: 1px solid var(--pz-hair); box-shadow: 0 18px 44px rgba(0,0,0,.34); animation: omPop .26s var(--ease) both; transform: translateX(-50%); white-space: nowrap; }
.pz__bartitle { font: 600 12.5px/1.2 var(--font); color: var(--pz-ink); }
.pz__vr { display: block; width: 1px; height: 26px; background: var(--pz-hair); }
.pz__choice { position: relative; display: flex; flex-direction: column; align-items: center; gap: 7px; padding: 0; border: 0; background: transparent; cursor: pointer; overflow: hidden; }
.pz__choicepic { display: block; width: 68px; height: 46px; border-radius: 11px; background-size: cover; background-position: center; box-shadow: inset 0 0 0 1px var(--pz-hair); transition: box-shadow .2s ease; }
.pz__choice.is-on .pz__choicepic { box-shadow: inset 0 0 0 2px var(--A), inset 0 0 0 4px var(--pz-bg); } /* (inset: the choice clips its overflow for the file input, and a ring outside showed along the bottom alone) */
.pz__choicepic--none { background: linear-gradient(135deg, var(--pz-bg) 47%, var(--pz-hair2) 48%, var(--pz-hair2) 52%, var(--pz-bg) 53%); }
.pz__choicepic--up { box-sizing: border-box; border: 1.5px dashed var(--pz-ink3); box-shadow: none; display: flex; align-items: center; justify-content: center; color: var(--pz-ink3); }
.pz__choice.is-on .pz__choicepic--up { border-color: var(--A); color: var(--A-read); box-shadow: none; }
.pz__choice--up input { position: absolute; inset: 0; width: 100%; height: 100%; opacity: 0; cursor: pointer; font-size: 0; }
.pz__choicename { font: 500 11.5px/1.2 var(--font); color: var(--pz-ink3); }
.pz__choice.is-on .pz__choicename { color: var(--pz-ink); font-weight: 600; }
.pz__bar2 .pz__choicename { display: none; }
.pz__bar2 .pz__choicepic { width: 42px; height: 30px; border-radius: 8px; }
.pz__barok { flex: none; width: 30px; height: 30px; border: 0; border-radius: 15px; background: var(--pz-field); cursor: pointer; display: flex; align-items: center; justify-content: center; color: var(--pz-ink2); }
.pz__textbtn { height: 28px; padding: 0 13px; border: 0; border-radius: 14px; background: transparent; cursor: pointer; font: 600 12px/1 var(--font); color: var(--A-read); transition: color .2s ease; }
.pz__textbtn:disabled { color: var(--pz-ink3); cursor: default; }
.pz__bar2 .pz__textbtn { height: 30px; padding: 0 12px; border-radius: 15px; background: var(--pz-field); }
.pz__choices { display: flex; flex-wrap: wrap; justify-content: center; align-items: flex-start; gap: 12px; }
/* the controls under the preview */
.pz__controls { flex: none; width: 100%; margin-top: 14px; display: flex; flex-direction: column; align-items: center; }
.pz__colour, .pz__courses, .pz__hcontrols { display: flex; flex-direction: column; align-items: center; gap: 10px; animation: omRise .34s var(--ease) .05s both; }
/* ready-made: four tiles, each the three scenes as they are placed (the sidebar down the left, the header over the counter) with the colour's dot */
.pz__ready { display: flex; align-items: center; justify-content: center; gap: 12px; flex-wrap: wrap; }
.pz__readyttl { font: 600 10.5px/1 var(--font); letter-spacing: .1em; text-transform: uppercase; color: var(--pz-ink3); margin-right: 4px; }
.pz__theme { display: flex; flex-direction: column; align-items: center; gap: 5px; padding: 0; border: 0; background: transparent; cursor: pointer; font: 500 11.5px/1 var(--font); color: var(--pz-ink2); }
.pz__theme.is-on { color: var(--pz-ink); font-weight: 600; }
.pz__thumb { position: relative; display: grid; grid-template-columns: 26px 1fr; grid-template-rows: 1fr 1fr; gap: 2px; width: 92px; height: 56px; padding: 2px; box-sizing: border-box; border-radius: 12px; overflow: hidden; background: var(--pz-tile); box-shadow: 0 0 0 2px transparent; transition: box-shadow .2s ease, transform .2s ease; }
.pz__theme:hover .pz__thumb { transform: translateY(-1px); }
.pz__theme.is-on .pz__thumb { box-shadow: 0 0 0 2px var(--ring, var(--c)); }
.pz__thumb--plain i:not(.pz__thumb-dot) { background: var(--pz-field); } /* (Default: the plain grounds, no photo anywhere) */
.pz__thumb i { display: block; border-radius: 6px; background: var(--pic) center / cover no-repeat; }
.pz__thumb-side { grid-row: 1 / 3; --pic: var(--pic-side); background-position: center bottom !important; }
.pz__thumb-head { --pic: var(--pic-head); background-position: right center !important; }
.pz__thumb-card { --pic: var(--pic-card); background-position: right bottom !important; }
.pz__thumb-dot { position: absolute; right: 6px; bottom: 6px; width: 10px; height: 10px; border-radius: 50%; background: var(--c); box-shadow: 0 0 0 2px rgba(255,255,255,.9); }
/* the first screen's preview sits a little to the left where the window has the room, so the eye lands on it before the controls */
@media (min-width: 1240px) { .pz[data-step="0"] .pz__pvwrap { transform: translateX(-40px); } } /* (the whole frame moves, so nothing is clipped) */
.pz__courses, .pz__hcontrols { gap: 20px; }
.pz__hfor { font: 600 13px/1.2 var(--font); color: var(--pz-ink); }
.pz__swatches { display: flex; flex-wrap: wrap; justify-content: center; gap: 14px; }
.pz__swwrap { position: relative; width: 52px; }
.pz__sw { display: flex; flex-direction: column; align-items: center; gap: 8px; border: 0; background: transparent; cursor: pointer; padding: 0; width: 52px; font: 500 11.5px/1.2 var(--font); color: var(--pz-ink3); transition: color .2s ease; }
.pz__sw.is-on { color: var(--pz-ink); font-weight: 600; }
.pz__swdot { display: block; width: 36px; height: 36px; border-radius: 18px; transition: box-shadow .2s ease; }
.pz__sw.is-on .pz__swdot { box-shadow: var(--A-ring); }
.pz__reads { display: flex; align-items: center; gap: 8px; }
.pz__aa { display: flex; align-items: center; justify-content: center; width: 30px; height: 22px; border-radius: 7px; font: 700 11px/1 var(--font); transition: color .3s ease; }
.pz__aa--light { background: #fff; border: 1px solid rgba(60,60,67,.14); color: var(--A-read-light); }
.pz__aa--dark { background: #1c1c1e; border: 1px solid rgba(255,255,255,.1); color: var(--A-read-dark); }
.pz__readnote { font: 400 12px/1.2 var(--font); color: var(--pz-ink3); }
.pz__tabs { display: flex; flex-wrap: wrap; justify-content: center; gap: 6px; padding: 4px; border-radius: 16px; background: var(--pz-tile); }
.pz__tab { display: flex; align-items: center; gap: 8px; height: 34px; padding: 0 14px; border: 0; border-radius: 12px; cursor: pointer; background: transparent; font: 500 13px/1 var(--font); color: var(--pz-ink2); transition: background .2s ease; }
.pz__tab.is-on { background: var(--pz-seg-on); box-shadow: 0 1px 3px rgba(0,0,0,.18); color: var(--pz-ink); font-weight: 600; }
.pz__tabdot { display: block; width: 9px; height: 9px; border-radius: 5px; background: var(--c); transition: background .3s ease; }
.pz__pal { display: flex; flex-wrap: wrap; justify-content: center; align-items: center; gap: 10px; max-width: 640px; }
.pz__palsw { width: 30px; height: 30px; border: 0; padding: 0; border-radius: 15px; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: box-shadow .2s ease; }
.pz__palsw svg { display: block; opacity: 0; transition: opacity .15s ease; }
.pz__palsw.is-on { box-shadow: 0 0 0 2px var(--pz-bg), 0 0 0 4px var(--pz-ink); }
.pz__palsw.is-on svg { opacity: 1; }
.pz__swwrap--course { width: 30px; height: 30px; }
.pz__empty { font: 400 14px/1.4 var(--font); color: var(--pz-ink3); text-align: center; }
/* the radial picker */
.pz__pk { position: absolute; left: 26px; top: 26px; z-index: 30; width: 272px; height: 272px; border-radius: 50%; background: var(--pk-bg); border: 1px solid var(--pz-hair); box-shadow: 0 24px 60px rgba(0,0,0,.4); touch-action: none; cursor: pointer; transform: translate(-50%, -50%); animation: omMorphIn .44s var(--ease) both; }
.pz__pk.is-closing { animation: omMorphOut .2s cubic-bezier(.4,0,1,1) both; }
.pz__pk--course { left: 15px; top: 15px; }
.pz__pkring { position: absolute; inset: 8px; border-radius: 50%; animation: omPkSpin .6s var(--ease) .1s both; background: conic-gradient(#ff0000, #ffff00, #00ff00, #00ffff, #0000ff, #ff00ff, #ff0000); -webkit-mask-image: radial-gradient(circle, transparent 107px, #000 108px, #000 127px, transparent 128px); mask-image: radial-gradient(circle, transparent 107px, #000 108px, #000 127px, transparent 128px); }
.pz__pksvg { position: absolute; inset: 0; display: block; overflow: visible; }
.pz__pktrack { animation: omPkDraw .5s cubic-bezier(.65,0,.35,1) .08s both; }
.pz__pktrack--dep { animation-delay: .13s; }
.pz__pklabel { font: 600 8.5px var(--font); letter-spacing: .14em; fill: var(--pz-ink2); animation: omPkLbl .3s ease both; }
.pz__pkknob { transform-box: fill-box; transform-origin: center; animation: omPkKnob .38s var(--ease) .26s both; }
.pz__pkknob--sat { animation-delay: .32s; } .pz__pkknob--dep { animation-delay: .37s; }
.pz__pkcore { position: absolute; left: 80px; top: 80px; width: 112px; height: 112px; border-radius: 50%; overflow: hidden; animation: omPkCore .45s var(--ease) .16s both; box-shadow: 0 0 0 3px var(--hex), 0 6px 18px rgba(0,0,0,.25); }
.pz__pkaa { position: absolute; left: 0; right: 0; height: 56px; display: flex; justify-content: center; font: 700 17px/1 var(--display); letter-spacing: -.02em; box-sizing: border-box; }
.pz__pkaa--light { top: 0; background: #fff; align-items: flex-start; padding-top: 14px; }
.pz__pkaa--dark { bottom: 0; background: #1c1c1e; align-items: flex-end; padding-bottom: 14px; }
.pz__pkdone { position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%); height: 26px; padding: 0 13px; border: 0; border-radius: 13px; background: var(--hex); color: var(--ink); cursor: pointer; font: 600 11.5px/1 var(--font); box-shadow: 0 2px 8px rgba(0,0,0,.28); }
/* the foot, and Saved */
.pz__foot { flex: none; width: 100%; margin-top: 12px; padding: 10px 0 14px; border-top: 1px solid var(--pz-hair); display: flex; align-items: center; gap: 10px; }
.pz__back { height: 42px; padding: 0 18px; border: 0; border-radius: 21px; background: transparent; color: var(--pz-ink2); cursor: pointer; font: 600 14px/1 var(--font); transition: background .2s ease; }
.pz__back:hover { background: var(--pz-tile); }
.pz__back--tile { background: var(--pz-tile); color: var(--pz-ink); padding: 0 20px; }
.pz__note { flex: 1; text-align: center; font: 400 12px/1.3 var(--font); color: var(--pz-ink3); }
.pz__next { min-width: 120px; height: 42px; padding: 0 22px; border: 0; border-radius: 21px; background: var(--A-btn); color: #fff; cursor: pointer; font: 600 14px/1 var(--font); transition: background .3s ease; animation: omRise .4s var(--ease) both; }
/* a still render (a swatch, a tab, the picker) redraws without the entry; a photo render fades the photo in alone */
.pz.is-still .pz__head, .pz.is-still .pz__colour, .pz.is-still .pz__courses, .pz.is-still .pz__hcontrols, .pz.is-still .pz__next, .pz.is-still .pz__hcard, .pz.is-still .pz__badge, .pz.is-still .pz__pic--sharp, .pz__bar2.is-kept { animation: none; }
.pz.is-still.is-photo .pz__pic--sharp { animation: omFade .35s ease both; }
.pz__done { align-self: center; width: 100%; max-width: 400px; padding: 80px 0 40px; display: flex; flex-direction: column; align-items: center; text-align: center; animation: omRise .4s var(--ease) both; }
.pz__donemark { width: 56px; height: 56px; border-radius: 28px; background: var(--A-btn); display: flex; align-items: center; justify-content: center; }
.pz__doneh1 { margin: 18px 0 0; font: 700 30px/1.1 var(--display); letter-spacing: -.035em; color: var(--pz-ink); }
.pz__donep { margin: 8px 0 0; font: 400 14px/1.5 var(--font); color: var(--pz-ink3); }
.pz__summary { margin-top: 22px; width: 100%; text-align: left; }
.pz__srow { display: flex; align-items: baseline; gap: 14px; padding: 12px 2px; border-top: 1px solid var(--pz-hair); }
.pz__sk { flex: 1; font: 400 13px/1.4 var(--font); color: var(--pz-ink3); }
.pz__sv { flex: none; font: 600 13px/1.4 var(--font); color: var(--pz-ink); }
.pz__donebtns { margin-top: 24px; display: flex; gap: 10px; }
@media (max-width: 700px) {
  .overlay .page--fr:has(.pz) { padding: 12px 14px 0; }
  .pz__name { display: none; }
  .pz__h1 { font-size: 20px; }
  .pz__pvwrap { height: min(430px, 44vw); }
  .pz__pk { left: 50%; top: 160px; }
  .pz__pk--course { left: 15px; top: 160px; }
}

/* ready: the read-back */
.page--fr .summary { margin-top: 22px; max-width: 520px; gap: 0; }
.page--fr .summary__row { align-items: baseline; gap: 16px; padding: 13px 0; border-top: 1px solid var(--hair); animation: frRise .32s var(--ease) both; }
.page--fr .summary__k { font: 400 14.5px/1.4 var(--font); color: var(--ink4); }
.page--fr .summary__v { max-width: 300px; font: 500 14.5px/1.4 var(--font); color: var(--ink); }

/* ==== What's new after an update: the setup's ground, one list — a heading per version, then its
 * notes — and Earlier versions as one button at the foot of the list ==== */
@keyframes frFade { from { opacity: 0; } to { opacity: 1; } }
.overlay--solid { --hue-new: #0a84ff; --tint-new: rgba(10,132,255,.1); --hue-improved: #1a6b30; --tint-improved: rgba(52,199,89,.14); --hue-fixed: #5b3ea8; --tint-fixed: rgba(140,110,240,.13); --accent-soft: rgba(10,132,255,.1); }
:host([data-theme="dark"]) .overlay--solid, html[data-theme="dark"] .overlay--solid { --tint-new: rgba(10,132,255,.18); --hue-improved: #5ddb7d; --tint-improved: rgba(52,199,89,.2); --hue-fixed: #c2a6f5; --tint-fixed: rgba(140,110,240,.2); --accent-soft: rgba(10,132,255,.18); }
.wn .fr__body { display: flex; flex-direction: column; min-height: 0; }
/* (the scrollbar shows whenever the list runs past its height, so the versions below are not missed) */
.wn__list { flex: 1; min-height: 0; max-height: 480px; padding-right: 10px; animation: frFade .26s ease both; scrollbar-width: thin; scrollbar-color: var(--thumb) var(--track); }
.wn__list::-webkit-scrollbar { width: 8px; height: auto; }
.wn__list::-webkit-scrollbar-track { background: var(--track); border-radius: 4px; }
.wn__list::-webkit-scrollbar-thumb { background: var(--thumb); border-radius: 4px; border: 0; }
.wn__list::-webkit-scrollbar-thumb:hover { background: var(--thumb-hot); }
.wn__list::-webkit-scrollbar-button { display: none; width: 0; height: 0; }
.wn__since { padding-bottom: 16px; font: 400 13px/1.4 var(--font); color: var(--ink4); }
.wn__vh { display: flex; align-items: baseline; gap: 10px; padding: 26px 0 8px; animation: frRise .34s var(--ease) both; }
.wn__vh:first-child, .wn__since + .wn__vh { padding-top: 0; }
.wn__vnum { font: 600 20px/1.2 var(--display); letter-spacing: -.024em; color: var(--ink); }
.wn__vdate { font: 400 12px/1.3 var(--font); color: var(--ink4); }
.wn__note { display: flex; gap: 16px; padding: 16px 0; border-top: 1px solid var(--hair); animation: frRise .34s var(--ease) both; --hue: var(--ink3); --tint: var(--tile); }
.wn__note[data-kind="new"] { --hue: var(--hue-new); --tint: var(--tint-new); }
.wn__note[data-kind="improved"] { --hue: var(--hue-improved); --tint: var(--tint-improved); }
.wn__note[data-kind="fixed"] { --hue: var(--hue-fixed); --tint: var(--tint-fixed); }
.wn__ic { flex: none; width: 34px; height: 34px; border-radius: 10px; background: var(--tint); color: var(--hue); display: flex; align-items: center; justify-content: center; }
.wn__ic svg { display: block; }
.wn__nbody { flex: 1; min-width: 0; }
.wn__title { display: block; font: 600 15.5px/1.3 var(--font); letter-spacing: -.016em; color: var(--ink); }
.wn__text { display: block; margin-top: 4px; font: 400 13px/1.55 var(--font); color: var(--ink3); text-wrap: pretty; }
.wn__more { display: block; width: 100%; margin-top: 4px; padding: 14px 0 6px; border: 0; border-top: 1px solid var(--hair); background: transparent; cursor: pointer; text-align: left; font: 500 13px/1.3 var(--font); color: var(--blue); }
.wn__more:hover { text-decoration: underline; }
.wn .fr__foot { padding-top: 22px; }

/* ==== the theme invitation (a release whose entry carries invite, in the notes' place): the four
 * scenes and the seven colours in a strip, a title, a line, then Not now and Personalize ==== */
.inv .fr__body { display: block; }
.inv__strip { display: flex; align-items: center; gap: 10px; margin-bottom: 28px; animation: frRise .34s var(--ease) both; }
.inv__scene { flex: none; width: 104px; height: 68px; border-radius: 14px; background: var(--tile) center / cover no-repeat; box-shadow: inset 0 0 0 1px rgba(0,0,0,.06); }
.inv__dots { display: flex; align-items: center; gap: 8px; margin-left: 12px; }
.inv__dot { width: 18px; height: 18px; border-radius: 9px; }
.inv .fr__h1 { font-size: 42px; animation: frRise .34s var(--ease) .06s both; }
.inv .fr__blurb { max-width: 620px; animation: frRise .34s var(--ease) .12s both; }
.inv .fr__foot { padding-top: 30px; }
@media (max-width: 700px) {
  .inv__strip { gap: 8px; margin-bottom: 20px; }
  .inv__scene { width: 0; flex: 1 1 0; height: 52px; border-radius: 10px; } /* (the four across the phone's width) */
  .inv__dots { display: none; }
  .inv .fr__h1 { font-size: 30px; }
}

@media (max-width: 700px) {
  .wn__list { max-height: 56vh; }
}
@media (max-width: 700px) {
  .overlay .page--fr { padding: 22px 16px 32px; }
  .fr { gap: 20px; }
  .fr__cols { min-height: 0; }
  .rail { display: none; }
  .fr__main { padding-left: 0; }
  .fr__foot { gap: 12px; }
  .tiles { flex-direction: column; }
  .page--fr .row__nick { width: 72px; }
  .page--fr .rows { max-height: 48vh; }
  .page--fr .targets { max-height: 40vh; }
  .page--fr .seg button { min-width: 26px; }
  .intro__svg { width: 250px; height: auto; }
  /* the theme step: the preview two counters wide, the picker in one column */
  .tpv { height: auto; min-height: 300px; }
  .tpv__side { width: 112px; min-width: 0; padding: 10px 6px 8px; }
  .tpv__cards { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 6px; margin: 10px 10px 0; }
  .tpv__head { padding: 10px 10px 8px; }
  .tpv__ghost { margin: 8px 10px 0; min-height: 40px; }
  .tpv__card { min-height: 52px; padding: 7px 8px; }
  .tpick__cols { flex-direction: column; gap: 14px; }
  .tpick__wheelwrap { width: 100%; }
  .tpick__wheel { width: 170px; height: 170px; }
  .tpick__reads { width: 100%; }
  .tpick__sl { grid-template-columns: 66px 1fr; }
  .tpick__hexrow, .tpick--sliders .tpick__hexrow { padding-left: 0; }
  .cc__swatch { width: 26px; height: 26px; }
  .cc__sw { padding-left: 0; }
  .thd__row { height: 60px; padding: 0 12px 0 14px; }
  .thd__title { font-size: 20px; }
}
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation: none !important; transition: none !important; }
  .mark--lg .arc, .done__check .arc { stroke-dashoffset: 0; }
  .card__body.is-entering { opacity: 1; transform: none; }
}

/* ---- the page after install: black, a splash, then an arrow and a line or two per screen ---- */
html.is-splash, html.is-splash body { background: #000; color: #fff; animation: none; transition: none; } /* black from the first paint: the class is on the page itself, so nothing fades to it */
.splash { position: fixed; inset: 0; background: #000; color: #fff; overflow: hidden; font-family: var(--font); }
.splash__stage { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 24px; text-align: center; animation: splashIn .4s var(--ease) both; }
.splash__stage.is-leaving { animation: splashOut .25s ease both; pointer-events: none; }
@keyframes splashIn { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: none; } }
@keyframes splashOut { to { opacity: 0; transform: translateY(-8px); } }
.splash__brand { display: flex; align-items: center; gap: 22px; }
.mark--splash { width: 84px; height: 84px; padding: 12px; border-radius: 22px; box-shadow: 0 16px 40px rgba(10,79,214,.45); animation: pop .6s var(--spring) both; }
.mark--splash svg { display: block; width: 100%; height: 100%; }
.splash__word { font: 700 64px/1 var(--display); letter-spacing: -.045em; color: #fff; animation: fadeUp .5s var(--ease) .25s both; }
.splash__text { position: relative; display: flex; flex-direction: column; align-items: center; gap: 10px; max-width: 540px; }
.splash__stage--pin .splash__text { margin-top: 22vh; }
.splash__kicker { font: 500 15px/1.3 var(--font); color: rgba(255,255,255,.55); animation: fadeUp .45s var(--ease) .1s both; }
.splash__title { margin: 0; font: 700 42px/1.1 var(--display); letter-spacing: -.035em; color: #fff; text-wrap: balance; animation: fadeUp .45s var(--ease) .18s both; }
.splash__hint { margin: 0; font: 400 17px/1.5 var(--font); color: rgba(255,255,255,.62); text-wrap: pretty; animation: fadeUp .45s var(--ease) .26s both; }
.splash__hint b { font-weight: 600; color: rgba(255,255,255,.88); }
.splash__btn { margin-top: 22px; height: 46px; padding: 0 26px; border: 0; border-radius: 23px; background: #fff; color: #000; cursor: pointer; font: 600 15.5px/1 var(--font); animation: fadeUp .4s var(--ease) both; transition: transform .18s var(--spring), box-shadow .25s ease; }
.splash__btn:hover { transform: translateY(-1px); box-shadow: 0 8px 22px rgba(255,255,255,.18); }
.splash__btn:active { transform: scale(.97); }
.splash__btn[hidden] { display: none; }
.splash__note { margin: 14px 0 0; font: 400 14px/1.4 var(--font); color: rgba(255,255,255,.5); }
.splash__note[hidden] { display: none; }
.splash__arrow { position: absolute; inset: 0; width: 100%; height: 100%; pointer-events: none; overflow: visible; }
.splash__arrow path { fill: none; stroke: #fff; stroke-width: 3; stroke-linecap: round; stroke-linejoin: round; }
.splash__arrow .splash__line { animation: drawStroke 1s var(--ease) .3s both; }
.splash__arrow .splash__head { animation: fadeIn .3s ease 1.1s both; }
@media (max-width: 600px) { .splash__brand { gap: 16px; } .mark--splash { width: 64px; height: 64px; padding: 9px; border-radius: 17px; } .splash__word { font-size: 48px; } .splash__title { font-size: 30px; } .splash__hint { font-size: 15px; } }
@media (prefers-reduced-motion: reduce) {
  .splash__stage, .splash__stage.is-leaving, .mark--splash, .splash__word, .splash__kicker, .splash__title, .splash__hint, .splash__btn, .splash__arrow .splash__line, .splash__arrow .splash__head { animation: none; }
  .splash__arrow .splash__line { stroke-dashoffset: 0 !important; }
}
`;

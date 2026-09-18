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
.fr { width: 100%; max-width: 880px; display: flex; flex-direction: column; gap: 26px; opacity: 0; }
.fr.is-in { opacity: 1; animation: frRise .44s var(--ease) both; }
.fr__top { display: flex; align-items: center; gap: 11px; }
.fr__brand { flex: none; display: flex; align-items: center; gap: 11px; border: 0; background: transparent; padding: 0; cursor: pointer; font: 500 13.5px/1.2 var(--font); letter-spacing: -.012em; color: var(--ink3); }
.fr__brand svg { display: block; flex: none; }
.fr__arc1 { stroke: var(--ink); }
.fr__arc2 { stroke: var(--ink4); }
.fr__spacer { flex: 1; }
.fr__count { flex: none; font: 500 12px/1.2 var(--font); color: var(--ink4); }
.fr__cols { display: flex; align-items: stretch; min-height: 436px; }
.rail { flex: none; width: 226px; padding-right: 26px; border-right: 1px solid var(--hair); display: flex; flex-direction: column; gap: 3px; }
.rail__item { display: flex; align-items: flex-start; gap: 12px; width: 100%; padding: 11px 12px; border: 0; border-radius: 11px; background: transparent; cursor: pointer; text-align: left; color: inherit; opacity: 1; transition: background .2s ease; }
.rail__item.is-active { background: var(--rail-on); }
.rail__item.is-locked, .rail__item:disabled { cursor: default; }
.rail__mark { flex: none; width: 19px; height: 19px; border-radius: 10px; margin-top: 1px; border: 1px solid var(--hair); display: flex; align-items: center; justify-content: center; font: 600 10px/1 var(--mono); color: var(--ink4); transition: background .2s ease, border-color .2s ease; }
.rail__item.is-active .rail__mark { border-color: var(--blue); color: var(--ink); }
.rail__item.is-done .rail__mark { background: var(--blue); border-color: var(--blue); }
.rail__mark svg { display: block; }
.rail__body { flex: 1; min-width: 0; }
.rail__name { display: block; font: 400 13.5px/1.3 var(--font); letter-spacing: -.012em; color: var(--ink2); transition: color .2s ease; }
.rail__item.is-active .rail__name { font-weight: 600; color: var(--ink); }
.rail__item.is-locked .rail__name { color: var(--ink4); }
.rail__answer { display: block; margin-top: 3px; font: 400 11.5px/1.3 var(--font); color: var(--ink4); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.rail__item.is-active .rail__answer { color: var(--ink3); }
.fr__main { flex: 1; min-width: 0; padding-left: 30px; display: flex; flex-direction: column; }
.fr__body { flex: 1; min-height: 0; transition: opacity .15s ease, transform .15s ease; }
.fr__body.is-leaving { opacity: 0; transform: translateX(var(--leave, -14px)); }
.fr__body.is-rising { animation: frRise .3s var(--ease) both; }
.fr__h1 { margin: 0; font: 600 26px/1.2 var(--display); letter-spacing: -.03em; color: var(--ink); text-wrap: pretty; }
.fr__blurb { margin: 9px 0 0; max-width: 430px; font: 400 13.5px/1.55 var(--font); color: var(--ink3); text-wrap: pretty; }
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
.page--fr .rows { margin-top: 4px; max-height: 300px; gap: 2px; overflow-y: scroll; padding-right: 8px; scrollbar-width: thin; scrollbar-color: var(--thumb) var(--track); }
.page--fr .rows::-webkit-scrollbar { width: 8px; height: auto; }
.page--fr .rows::-webkit-scrollbar-track { background: var(--track); border-radius: 4px; }
.page--fr .rows::-webkit-scrollbar-thumb { background: var(--thumb); border-radius: 4px; border: 0; }
.page--fr .rows::-webkit-scrollbar-thumb:hover { background: var(--thumb-hot); }
.page--fr .rows::-webkit-scrollbar-button { display: none; width: 0; height: 0; }
.page--fr .row { gap: 13px; padding: 11px 12px 11px 11px; border: 0; border-radius: 11px; background: transparent; animation: frRise .32s var(--ease) both; transition: background .2s ease; }
.page--fr .row:active { transform: none; }
.page--fr .row.is-on { background: var(--row-on); border: 0; padding: 11px 12px 11px 11px; }
.page--fr .row__box { width: 20px; height: 20px; border-radius: 6px; border: 1.5px solid var(--box-edge); transition: background .18s ease, border-color .18s ease; }
.page--fr .row__box svg { transform: none; transition: opacity .16s ease; }
.page--fr .row__dot { width: 6px; height: 6px; border-radius: 3px; opacity: .3; transition: opacity .2s ease; }
.page--fr .row.is-on .row__dot { opacity: 1; }
.page--fr .row__code { font: 400 14px/1.3 var(--font); letter-spacing: -.012em; color: var(--ink3); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; transition: color .2s ease; }
.page--fr .row.is-on .row__code { font-weight: 600; color: var(--ink); }
.page--fr .row__name { margin-top: 2px; font: 400 11px/1.3 var(--font); color: var(--ink4); }
.page--fr .row__nick { flex: none; width: 92px; height: auto; border: 0; border-bottom: 1px solid var(--hair); border-radius: 0; background: transparent; padding: 0 0 3px; font: 400 12.5px/1.3 var(--font); color: var(--ink2); text-align: right; }
.page--fr .row__nick:focus { border-color: var(--blue); }
.page--fr .row:not(.is-on) .row__nick { display: none; }
.page--fr .ghosts { margin-top: 22px; gap: 2px; }
.page--fr .ghost { height: 52px; border-radius: 11px; background: var(--tile); }
.page--fr .scanning { color: var(--ink4); }
.page--fr .empty { background: var(--tile); border-color: var(--hair); }

/* 2 · grades */
.fr__lines { margin-top: 22px; }
.fr__line { display: flex; align-items: center; gap: 16px; padding: 15px 0; border-top: 1px solid var(--hair); animation: frRise .28s var(--ease) both; }
.fr__line.is-hidden { display: none; }
.fr__linebody { flex: 1; min-width: 0; }
.fr__linet { display: block; font: 500 14.5px/1.3 var(--font); letter-spacing: -.012em; color: var(--ink); }
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
.page--fr .targets { max-height: 198px; }
.page--fr .target { gap: 13px; padding: 12px 0; border: 0; border-top: 1px solid var(--hair); border-radius: 0; background: transparent; animation: frRise .32s var(--ease) both; }
.page--fr .target + .target { margin-top: 0; }
.page--fr .target .row__dot { opacity: 1; }
.page--fr .target__code { font: 500 14px/1.3 var(--font); letter-spacing: -.012em; }
.page--fr .seg { gap: 1px; padding: 0; border: 0; border-radius: 0; background: transparent; }
.page--fr .seg button { min-width: 30px; height: 26px; border-radius: 8px; font: 400 12.5px/1 var(--font); color: var(--ink4); }
.page--fr .seg button.is-on { background: var(--blue); color: #fff; font-weight: 600; box-shadow: none; }

/* 3 · 4 · the preview tiles: miniatures of the real layouts in the student's own course colours */
.tiles { margin-top: 24px; display: flex; gap: 12px; }
.tiles--2 { gap: 16px; max-width: 520px; }
.tile { flex: 1; min-width: 0; padding: 0; border: 0; background: transparent; cursor: pointer; text-align: left; display: flex; flex-direction: column; gap: 11px; color: inherit; font: inherit; }
.tile__frame { display: block; height: 108px; padding: 12px; border: 1px solid var(--hair); border-radius: 13px; background: var(--tile); transition: border-color .2s ease, box-shadow .2s ease; }
.tile[data-view="cards"] .tile__frame, .tiles--2 .tile__frame { padding: 10px; }
.tiles--2 .tile__frame { height: 132px; }
.tile.is-on .tile__frame { border-color: var(--blue); box-shadow: inset 0 0 0 1px var(--blue); }
.tile__foot { display: flex; align-items: flex-start; gap: 8px; }
.tile__body { flex: 1; min-width: 0; }
.tile__t { display: block; font: 400 14px/1.3 var(--font); color: var(--ink3); transition: color .2s ease; }
.tile.is-on .tile__t { font-weight: 600; color: var(--ink); }
.tile__s { display: block; margin-top: 3px; font: 400 11.5px/1.4 var(--font); color: var(--ink4); text-wrap: pretty; }
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

/* ready: the read-back */
.page--fr .summary { margin-top: 22px; max-width: 420px; gap: 0; }
.page--fr .summary__row { align-items: baseline; gap: 16px; padding: 13px 0; border-top: 1px solid var(--hair); animation: frRise .32s var(--ease) both; }
.page--fr .summary__k { font: 400 13px/1.4 var(--font); color: var(--ink4); }
.page--fr .summary__v { max-width: 230px; font: 500 13px/1.4 var(--font); color: var(--ink); }

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
}
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation: none !important; transition: none !important; }
  .mark--lg .arc, .done__check .arc { stroke-dashoffset: 0; }
  .card__body.is-entering { opacity: 1; transform: none; }
}

/* ---- the page after install: black, a splash, then an arrow and a line or two per screen ---- */
html.is-splash, html.is-splash body { background: #000; color: #fff; animation: none; }
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

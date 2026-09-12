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
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation: none !important; transition: none !important; }
  .mark--lg .arc, .done__check .arc { stroke-dashoffset: 0; }
  .card__body.is-entering { opacity: 1; transform: none; }
}
`;

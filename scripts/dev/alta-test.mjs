// Simpl for Mac: the Knewton Alta popup's page script (mac/Simpl/Engine/AltaSession.swift, AltaHook.source).
// First in a stand-in page: it does nothing on any site but Alta's. Then in Chromium on the mock's Alta player
// (scripts/dev/mock-canvas.mjs, /mock-alta/…): it passes the app the assignment, its objectives and their mastery —
// from Alta's own answers, the overview's words and Alta's mastery bars — never the question, the answer key or who
// the student is. (1.3.45) It only reads: the page is worked by the student, never answered, pressed or restyled.
//   node scripts/dev/alta-test.mjs
import { existsSync, readFileSync } from 'node:fs';
import { spawn, execSync } from 'node:child_process';
import { createRequire } from 'node:module';
import { join } from 'node:path';
import vm from 'node:vm';
import net from 'node:net';

const ROOT = new URL('../../', import.meta.url).pathname;
const swift = readFileSync(join(ROOT, 'mac/Simpl/Engine/AltaSession.swift'), 'utf8');
const m = /static let source = #"""\n([\s\S]*?)\n\s*"""#/.exec(swift);
if (!m) { console.error('AltaHook.source not found in AltaSession.swift'); process.exit(1); }
const source = m[1];
const c = /static let collect = #"""\n([\s\S]*?)\n\s*"""#/.exec(swift);
if (!c) { console.error('the Save Page Code script was not found in AltaSession.swift'); process.exit(1); }
const collect = c[1];

let failed = 0;
const check = (ok, what) => { console.log(`  ${ok ? '✓' : '✗'} ${what}`); if (!ok) failed++; };
const SECRETS = ['student-secret-id', 'reg-secret', 'lti-secret', 'school.example', 'correct_answer', 'success_condition', 'valid_response', 'a=3'];

console.log('the Alta popup’s page script, off the web');
{
  const posted = [];
  const realFetch = async () => new Response('{}', { headers: { 'content-type': 'application/json' } });
  const win = { location: { protocol: 'about:', hostname: '', pathname: 'blank' }, fetch: realFetch, XMLHttpRequest: function () {}, history: {}, addEventListener() {}, webkit: { messageHandlers: { simplAlta: { postMessage: (s) => posted.push(s) } } } };
  win.XMLHttpRequest.prototype = { open() {}, send() {} };
  const open0 = win.XMLHttpRequest.prototype.open;
  win.window = win;
  vm.createContext(win);
  vm.runInContext(source, win);
  check(posted.length === 0 && win.fetch === realFetch && win.XMLHttpRequest.prototype.open === open0, 'on a page that is not a web page (about:blank) it does nothing (it is put only in the popup’s own view, which holds Alta’s launch)');
}

console.log('\nin Chromium, on the mock’s Alta player');
// (1.3.83) ports of its own, free now: another checkout's copy of this suite may be running beside it on 8881, and its
// mock answered this one's questions
const freePort = () => new Promise((done) => { const s = net.createServer(); s.listen(0, '127.0.0.1', () => { const p = s.address().port; s.close(() => done(p)); }); });
const PORT = await freePort(), SIM = await freePort();
// (1.3.102: with the launch as Canvas makes it — its own page first, whose form sends itself on to Alta)
const mock = spawn(process.execPath, [join(ROOT, 'scripts/dev/mock-canvas.mjs'), String(PORT), String(SIM), '--alta-launch'], { stdio: 'ignore' });
const stop = () => { try { mock.kill(); } catch { /* gone */ } };
process.on('exit', stop);
const BASE = `http://localhost:${PORT}`;
for (let i = 0; i < 50; i++) { try { await fetch(`${BASE}/mock-alta/api/reset`); break; } catch { await new Promise((r) => setTimeout(r, 100)); } }
await fetch(`${BASE}/mock-alta/api/reset`);

const { chromium } = createRequire(join(execSync('npm root -g').toString().trim(), 'x.js'))('playwright');
const browser = await chromium.launch();
try {
  const page = await browser.newPage();
  await page.addInitScript(() => { window.__posted = []; window.webkit = { messageHandlers: { simplAlta: { postMessage: (s) => window.__posted.push(s) } } }; });
  await page.addInitScript({ content: source });
  const posts = async () => (await page.evaluate(() => window.__posted.slice())).map((s) => JSON.parse(s));
  const waitFor = async (test, what, tries = 60) => {
    for (let i = 0; i < tries; i++) { const p = (await posts()).filter(test); if (p.length) return p[p.length - 1]; await page.waitForTimeout(100); }
    throw new Error(`no ${what} came`);
  };

  // (1.3.19) an overview that names its objectives only in its words
  await page.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a2`);
  const w2 = await waitFor((p) => p.kind === 'overview' && p.objectives?.length, 'overview read from the page’s words');
  check(w2.name === 'Differentiation Rules 2' && w2.statusText === 'Not started' && w2.dueText === 'Monday, Oct 12 11:59pm PDT',
    `on an overview drawn in words: its title, due date and status read (${w2.dueText} · ${w2.statusText})`);
  check(w2.objectives.length === 2 && w2.objectives[0].name === 'Combine the product and quotient rules' && w2.objectives[0].low === 4 && w2.objectives[0].high === 9 && w2.objectives[1].high === 6,
    `and its objectives, each under its “Estimated … questions” (${w2.objectives.map((o) => `${o.name} ${o.low}–${o.high}`).join(' · ')})`);

  // (1.3.46) Save Page Code: the page's HTML, its web components' own trees, and each script and style sheet it loads
  const code = await page.evaluate(`(async () => { ${collect} })()`);
  check(code.url === `${BASE}/mock-alta/learn/course/c1/assignment/a2` && /^<!DOCTYPE html>\n<html/.test(code.html) && code.html.includes('Differentiation Rules 2'),
    `Save Page Code takes the page’s whole HTML, its address without a query (${code.html.length} characters)`);
  check(code.shadows.length >= 1 && code.shadows.some((s) => /START/.test(s.html)), `and each web component’s own tree (${code.shadows.map((s) => s.host).join(', ')})`);
  await page.evaluate(() => { const s = document.createElement('script'); s.src = '/mock-alta/api/assignment?x=1'; document.head.appendChild(s); const l = document.createElement('link'); l.rel = 'stylesheet'; l.href = 'http://127.0.0.1:9/gone.css'; document.head.appendChild(l); });
  const code2 = await page.evaluate(`(async () => { ${collect} })()`);
  const js = code2.files.find((f) => f.kind === 'script'), css = code2.files.find((f) => f.kind === 'style sheet');
  check(js && js.url.endsWith('/mock-alta/api/assignment') && /Differentiation Rules 2/.test(js.text) && css && !css.text && !!css.error && css.url === 'http://127.0.0.1:9/gone.css',
    `and the text of each script and style sheet it loads, or why one could not be read (${js?.url} · ${css?.error})`);

  // (1.3.59) the assignment's cover, read for the popup's own start screen: its name, details, note, Start and Review
  // Instruction, and its objectives by topic with each topic's estimate and each objective's mastery
  await page.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a4/cover`);
  const cv = await waitFor((p) => p.kind === 'cover' && !p.none, 'the cover');
  check(!!cv && cv.name === 'Limits at a Point' && cv.details.length === 3 && cv.details[0].label === 'Due Date' && cv.details[0].value === 'Monday, Oct 12 11:59pm PDT'
    && cv.details[1].value === 'Not started' && cv.details[2].label === 'Grade' && cv.details[2].value === 'No credit' && cv.details[2].sub === 'Last updated Oct 10 at 8:08pm',
    `the cover read: its name and details (${cv && cv.details.map((d) => d.label + ': ' + d.value).join(' · ')})`);
  check(!!cv && cv.note === 'Complete by 11:59pm today to earn 100% additional credit.' && cv.start === 'Start' && cv.review === true,
    `its note, its Start as Alta words it, and Review Instruction (${cv && cv.start})`);
  check(!!cv && cv.topics.length === 2 && cv.topics[0].name === 'The Limit of a Function' && cv.topics[0].estimate === '6\u201312 questions' && cv.topics[0].low === 6 && cv.topics[0].high === 12
    && JSON.stringify(cv.topics.map((t) => t.objectives.map((o) => o.mastery))) === JSON.stringify([[0, 0.4], [1]]) && cv.topics[1].objectives[0].name === 'Find a one-sided limit',
    `its objectives by topic, each topic's estimate, each objective's mastery (${cv && cv.topics.map((t) => t.name + ' ' + t.estimate + ' [' + t.objectives.map((o) => o.name).join(', ') + ']').join(' · ')})`);
  check(await page.evaluate(() => window.__simplCover.start()), 'the app\'s Start presses Alta\'s');
  await page.waitForURL(/assignment\/a3$/, { timeout: 8000 }).catch(() => {});
  check(/assignment\/a3$/.test(page.url()), 'and Alta moves on to the assignment');

  // (1.3.47) Alta's question alone, on its learn page as it is really built: everything else hidden
  await page.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a3`);
  const vis = async (sel) => page.$eval(sel, (e) => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; }).catch(() => false);
  const fq = await waitFor((p) => p.kind === 'focus' && p.state === 'question', 'the question recognised');
  check(!!fq && fq.shape === null, 'Alta’s learn page is recognised: one part, headed Question, with Learnosity’s answer box');
  const hiddenAll = !(await vis('header')) && !(await vis('h1')) && !(await vis('.jss179')) && !(await vis('.knewcons__knewcons___e3wUn')) && !(await vis('#embedded-messaging')) && !(await vis('.back-to-top-mount')) && !(await vis('#skip-to-main'));
  check(hiddenAll, 'the app bar, title and mastery, Current objective card, banner, chat and Back to Top are hidden');
  check((await vis('#asks')) && (await vis('#answer')) && (await vis('.lrn-formula-keyboard')) && (await vis('.content-title__heading___1PTj4')),
    'the question stays whole: its heading, what it asks, Learnosity’s answer box and keypad (its buttons go to the app’s bar)');
  check(!(await vis('#fb')) && !(await vis('#attr')), 'Alta’s Feedback (a report to Alta) and Content attribution are put away');
  // (1.3.53) every button in the parts' bars, whatever it says, to the app — its words, Alta's main one, whether it can be
  // pressed — the bar put away; and the question's words and its answer as two panels, in the page's coordinates
  const bt = await waitFor((p) => p.kind === 'buttons' && p.items.length, 'the buttons');
  check(JSON.stringify(bt.items) === JSON.stringify([{ label: 'More Instruction', primary: false, enabled: true }, { label: 'Submit', primary: true, enabled: false }]) && !(await vis('#more')) && !(await vis('#submit')),
    `the bar's buttons go to the app, whatever they say — Submit Alta's main one, greyed out — and Alta's bar is put away (${JSON.stringify(bt.items)})`);
  await page.evaluate(() => { document.getElementById('submit').disabled = false; });
  const bt2 = await waitFor((p) => p.kind === 'buttons' && p.items[1] && p.items[1].enabled === true, 'Submit ready');
  check(!!bt2, 'when Alta’s Submit can be pressed, the app is told');
  await page.evaluate(() => { window.__pressed = ''; document.getElementById('more').onclick = () => { window.__pressed = 'more'; }; });
  check((await page.evaluate(() => window.__simplButtons.press(0))) && (await page.evaluate(() => window.__pressed)) === 'more', 'the app’s button presses Alta’s');
  await page.evaluate(() => { document.getElementById('submit').disabled = true; });
  const pn = await waitFor((p) => p.kind === 'panels' && p.panels.length === 2, 'the panels');
  const [pq, pa] = pn.panels;
  check(pq.kind === 'question' && pa.kind === 'answer' && pq.y < pa.y && pa.y - (pq.y + pq.h) >= 40 && pn.height >= pa.y + pa.h && pq.w > 200,
    `what the question asks and its answer are two panels with a gap between, and the page's height is told (${JSON.stringify(pn)})`);
  // (1.3.62) a graph drawn into a part (Learnosity's JSXGraph box, its labels in it) and a large SVG are marked to be
  // turned back whole in dark mode; a small icon is not
  await page.evaluate(() => { document.querySelector('section[data-simpl-keep]').insertAdjacentHTML('beforeend', '<div class="lrn_graphplotting"><div class="jxgbox" style="width:300px;height:200px"><svg width="300" height="200"></svg><div class="JXGtext">(2, 0)</div></div></div><svg id="plot" width="240" height="160"></svg><svg id="icon" width="16" height="16"></svg><img id="gimg" alt="" style="display:block;width:220px;height:150px"><svg id="big" width="1280" height="840"></svg>'); });
  await page.waitForTimeout(600);
  check(await page.evaluate(() => { const g = document.querySelector('.lrn_graphplotting'); return g.hasAttribute('data-simpl-pic') && !g.querySelector('[data-simpl-pic]') && document.getElementById('plot').hasAttribute('data-simpl-pic') && document.getElementById('gimg').hasAttribute('data-simpl-pic') && !document.getElementById('icon').hasAttribute('data-simpl-pic'); }),
    'a graph’s box (with its labels) and a large drawing are marked to keep their own colours in dark mode; an icon is not');
  // (1.3.70) a large graph in what the question asks is drawn small (600 × 440 at most since 1.3.98/0.3.18; 300 × 220 before), its place told to the app for the
  // button by it, and a click on it asks the app to open it large; its rect and zoom are there for the picture
  const nm = (await posts()).length;
  const mini = await page.evaluate(() => { const b = document.getElementById('big'); const r = b.getBoundingClientRect(); return { on: b.hasAttribute('data-simpl-mini'), w: r.width, h: r.height, z: b.style.getPropertyValue('--simpl-zoom') }; });
  const pm = await waitFor((p, k) => k >= nm && p.kind === 'panels' && Array.isArray(p.pics) && p.pics.length === 1, 'the small graph’s place', 30).catch(() => null) || (await posts()).filter((p) => p.kind === 'panels').pop();
  check(mini.on && mini.w <= 602 && mini.h <= 442 && +mini.z < 1 && pm?.pics?.length === 1 && Math.abs(pm.pics[0].w - mini.w) < 2,
    `a large graph is drawn small, its place told to the app (${JSON.stringify({ mini, pics: pm?.pics })})`);
  await page.click('#big');
  const po = await waitFor((p) => p.kind === 'picOpen', 'the graph asked open', 20);
  const pr = await page.evaluate(() => window.__simplPic.rect(0));
  check(po.i === 0 && pr && pr.w <= 602 && pr.z < 1, `a click on it asks the app to open it large, with where it is and its zoom (${JSON.stringify(pr)})`);
  // (1.3.70) dark mode: graphs dark by default (turned over with the question), as Alta draws them on Light
  await page.evaluate(() => { window.__simplDark = true; window.__simplPicsLight = false; window.__simplFocusDark(); });
  const gDark = await page.evaluate(() => getComputedStyle(document.getElementById('gimg')).filter);
  await page.evaluate(() => { window.__simplPicsLight = true; window.__simplFocusDark(); });
  const gLight = await page.evaluate(() => ({ f: getComputedStyle(document.getElementById('gimg')).filter, bg: getComputedStyle(document.getElementById('gimg')).backgroundColor }));
  await page.evaluate(() => { window.__simplDark = false; window.__simplPicsLight = false; window.__simplFocusDark(); });
  check(gDark === 'none' && /invert\(1\)/.test(gLight.f) && gLight.bg === 'rgb(255, 255, 255)', `in dark mode a graph is dark by default, and on Light as Alta draws it on white (${gDark} → ${gLight.f})`);
  await page.evaluate(() => { document.querySelector('.lrn_graphplotting').remove(); document.getElementById('plot').remove(); document.getElementById('icon').remove(); document.getElementById('gimg').remove(); document.getElementById('big').remove(); });
  await page.waitForTimeout(400);
  // (1.3.60) each part a centred column no wider than 860 points
  const vw = await page.evaluate(() => innerWidth);
  check(pq.w <= 861 && pa.w <= 861 && (vw < 900 || Math.abs((pq.x + pq.w / 2) - vw / 2) < 40),
    `the parts are a centred column no wider than 860 points (${Math.round(pq.w)} and ${Math.round(pa.w)} wide in ${vw})`);
  // (a sprite sheet in a part that holds nothing else kept — Alta's banner — draws but takes no room)
  await page.evaluate(() => { document.querySelector('.knewcons__knewcons___e3wUn').insertAdjacentHTML('beforeend', '<svg id="sp2" style="position:absolute;width:0;height:0"><defs><symbol id="s2"><circle r="3"/></symbol></defs></svg>'); });
  await page.waitForTimeout(600);
  check(await page.evaluate(() => { const k = document.querySelector('.knewcons__knewcons___e3wUn'); return k.hasAttribute('data-simpl-thin') && k.getBoundingClientRect().height === 0 && getComputedStyle(document.getElementById('sp2')).display !== 'none'; }),
    'a sprite sheet inside Alta’s banner is kept, the banner round it taking no room');
  await page.evaluate(() => document.getElementById('sp2').remove());
  check(await page.evaluate(() => getComputedStyle(document.getElementById('MathJax_Hidden')).display !== 'none' && getComputedStyle(document.getElementById('MathJax_Hidden').parentElement).display !== 'none'), 'MathJax’s own hidden helper is never hidden (it measures its fonts by it)');
  const top = await page.$eval('#qsec', (e) => Math.round(e.getBoundingClientRect().top));
  check(top < 60, `the question sits at the top, no gap above it (${top}px)`);
  check(await page.evaluate(() => { const q = document.getElementById('qsec'); return q.getAttribute('style') === null && !document.querySelector('[style*="-10000px"]'); }), 'nothing of Alta’s is moved or restyled: only hidden by marks and a style sheet');
  // (1.3.49) laid on the popup's glass: no ground of the page's, its parts' or the question card's — Alta's own answer box keeps its
  const clear = await page.evaluate(() => {
    const bg = (el) => getComputedStyle(el).backgroundColor, sh = (el) => getComputedStyle(el).boxShadow;
    const card = document.querySelector('#qsec > .theme__card___3E6YM');
    return { html: bg(document.documentElement), body: bg(document.body), path: [...document.querySelectorAll('[data-simpl-path]')].every((e) => bg(e) === 'rgba(0, 0, 0, 0)'), card: bg(card), cardShadow: sh(card) };
  });
  check(clear.html === 'rgba(0, 0, 0, 0)' && clear.body === 'rgba(0, 0, 0, 0)' && clear.path && clear.card === 'rgba(0, 0, 0, 0)' && clear.cardShadow === 'none',
    `the page, the parts round the question and its card draw no ground, so the popup’s glass shows through (${JSON.stringify(clear)})`);
  // (1.3.50) dark mode, on dark glass: the question's colours turned over (light on dark, hues kept), its pictures turned back
  await page.evaluate(() => { document.getElementById('asks').insertAdjacentHTML('afterend', '<img id="pic" width="8" height="8" src="data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACwAAAAAAQABAAACAkQBADs=">'); window.__simplDark = true; window.__simplFocusDark(); });
  const dk = await page.evaluate(() => ({ attr: document.documentElement.hasAttribute('data-simpl-dark'), sec: getComputedStyle(document.getElementById('qsec')).filter, pic: getComputedStyle(document.getElementById('pic')).filter, head: getComputedStyle(document.querySelector('header')).display }));
  check(dk.attr && /invert\(1\)/.test(dk.sec) && /hue-rotate\(180deg\)/.test(dk.sec) && /invert\(1\)/.test(dk.pic),
    `in dark mode the question’s colours are turned over and its pictures turned back (${dk.sec} · picture ${dk.pic})`);
  await page.evaluate(() => { window.__simplDark = false; window.__simplFocusDark(); document.getElementById('pic').remove(); });
  check(await page.evaluate(() => !document.documentElement.hasAttribute('data-simpl-dark') && getComputedStyle(document.getElementById('qsec')).filter === 'none'), 'and in light mode, left as Alta draws them');
  // typing and scrolling are left alone
  await page.setViewportSize({ width: 900, height: 200 });
  await page.evaluate(() => window.scrollTo(0, 120));
  const sy0 = await page.evaluate(() => window.scrollY);
  for (let i = 0; i < 5; i++) { await page.evaluate((i) => { document.querySelector('.mq-root-block').textContent = 'x'.repeat(i + 1); }, i); await page.waitForTimeout(200); }
  const sy1 = await page.evaluate(() => window.scrollY);
  check(sy0 > 0 && sy0 === sy1, `typing in the answer leaves the page where it was scrolled (${sy0} → ${sy1})`);
  await page.setViewportSize({ width: 1280, height: 720 });
  // Alta's pop-ups over it stay in sight
  await page.evaluate(() => window.mockDialog());
  await page.waitForTimeout(400);
  check((await vis('#vi')) && (await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-q'))), 'a pop-up of Alta’s (Having trouble?) is shown over the question');
  // (1.3.51) Alta's pop-ups as the Mac's own: passed to the app (its words and buttons), hidden in the page on the app's word, its
  // buttons pressed from the app
  const dlg = await waitFor((p) => p.kind === 'dialog' && p.open === true, 'the pop-up passed to the app');
  check(/Having trouble/.test(dlg.text) && dlg.buttons.join() === 'VIEW INSTRUCTION' && (await page.evaluate(() => document.getElementById('dlg').hasAttribute('data-simpl-dialog'))),
    `an open pop-up of Alta's is passed to the app, its words and buttons (${JSON.stringify(dlg.buttons)})`);
  // (1.3.54) its picture taken without its buttons: hidden for that moment, the row at its foot cropped off
  const full = await page.evaluate(() => document.querySelector('#dlg [role=dialog]').getBoundingClientRect().height);
  await page.evaluate(() => window.__simplDialog.shot(true));
  const hiddenBtn = await page.evaluate(() => getComputedStyle(document.getElementById('vi')).visibility);
  await page.evaluate(() => window.__simplDialog.shot(false));
  check(hiddenBtn === 'hidden' && (await page.evaluate(() => getComputedStyle(document.getElementById('vi')).visibility)) === 'visible', 'for its picture, the pop-up’s own buttons are hidden for that moment');
  await page.evaluate(() => { const d = document.querySelector('#dlg [role=dialog]'); d.style.paddingBottom = '10px'; d.insertAdjacentHTML('afterbegin', '<p style="height:120px;margin:0">Welcome to your adaptive assignment!</p>'); });
  const cropped = await page.evaluate(() => window.__simplDialog.rect());
  const btnTop = await page.evaluate(() => document.getElementById('vi').getBoundingClientRect().top - document.querySelector('#dlg [role=dialog]').getBoundingClientRect().top);
  check(cropped.h <= btnTop && cropped.h > 100, `and its picture stops above the row of buttons at its foot (${cropped.h} px of ${Math.round(full + 120)}, buttons at ${Math.round(btnTop)})`);
  const rect = await page.evaluate(() => window.__simplDialog.rect());
  await page.evaluate(() => window.__simplDialog.show(false));
  check(rect && rect.w > 10 && rect.h > 10 && (await page.evaluate(() => getComputedStyle(document.querySelector('#dlg [role=dialog]')).visibility)) === 'hidden',
    'the app is told where it is (for its picture of it), and it is hidden in the page while the app shows it');
  await page.evaluate(() => { document.getElementById('vi').onclick = () => document.getElementById('dlg').remove(); });
  const np = (await posts()).length;
  check(await page.evaluate(() => window.__simplDialog.press(0)), 'the app’s button presses Alta’s');
  await waitFor((p, k) => k >= np && p.kind === 'dialog' && p.open === false, 'the pop-up gone');
  check(!(await page.$('#dlg')) && !(await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-native-dialog'))), 'and when it closes, the app is told and the page left as it was');
  // (the page's sprite sheet — an svg of symbols its pictures, a pop-up's among them, draw from — is never hidden)
  await page.evaluate(() => { const d = document.createElement('div'); d.innerHTML = '<svg id="sprite" style="position:absolute;width:0;height:0"><defs><symbol id="intro"><circle r="5"/></symbol></defs></svg>'; document.body.appendChild(d); });
  await page.waitForTimeout(500);
  check(await page.evaluate(() => getComputedStyle(document.getElementById('sprite')).display !== 'none' && getComputedStyle(document.getElementById('sprite').parentElement).display !== 'none'), 'the page’s sprite sheet is kept, so its pictures draw');
  await page.evaluate(() => document.getElementById('sprite').parentElement.remove());
  // (the learn page opened afresh for what follows: the page script shows a page nine seconds after it opens whatever
  // happens, and by now that time had often come in the middle of the check of the page out of sight between parts)
  await page.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a3`);
  await waitFor((p) => p.kind === 'focus' && p.state === 'question', 'the question recognised again');
  // (1.3.48) a lesson (More Instruction) above the question: both kept, Alta's header and the rest still hidden
  const nl = (await posts()).length;
  await page.evaluate(() => window.mockLesson());
  await page.waitForTimeout(500);
  check((await vis('#lsec')) && (await vis('#asks')) && !(await vis('header')) && !(await vis('h1')) && !(await vis('.jss179')),
    'a lesson above the question is shown with it, Alta’s header, title and objective card still hidden');
  // a lesson alone (its Continue): kept too
  await page.evaluate(() => { const q = document.getElementById('qsec'); window.__q = q; q.remove(); document.querySelector('#lsec .theme__card___3E6YM').insertAdjacentHTML('beforeend', '<div class="button-bar__container___H_Vc_"><button>Feedback</button><button id="cont">Continue</button></div>'); });
  const fl = await waitFor((p, k) => k >= nl && p.kind === 'focus' && p.state === 'lesson', 'a lesson recognised');
  const lb = await waitFor((p, k) => k >= nl && p.kind === 'buttons' && p.items.some((b) => b.label === 'Continue'), 'the lesson’s Continue');
  check(!!fl && !!lb && (await vis('#lsec')) && !(await vis('header')) && !(await vis('h1')), 'a lesson alone is shown alone, the header hidden, its Continue given to the app');
  // not recognised (Alta's learn page with no part in it): Alta's page back as it is, after a moment, and why
  const nb = (await posts()).length;
  await page.evaluate(() => { window.__l = document.getElementById('lsec'); window.__flow = window.__l.parentElement; window.__l.remove(); });
  await page.waitForTimeout(400);
  check(await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-q')), 'a moment’s grace first (Alta between parts)');
  // (1.3.62) and through it, out of sight (the app's Loading), never Alta's page as it is — the next part laid out as soon
  // as it comes, or (here, none) Alta's page as it is after a while
  const hid = await waitFor((p, k) => k >= nb && p.kind === 'hidden', 'out of sight between parts');
  check(!!hid && !(await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-shown'))) && (await page.evaluate(() => getComputedStyle(document.body).opacity)) === '0',
    'between parts the page is out of sight, the app told (its Loading shown)');
  const bad = await waitFor((p, k) => k >= nb && p.kind === 'focus' && p.state === 'unknown', 'the page not recognised', 110);
  check((await vis('header')) && (await vis('h1')) && !(await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-q'))) && (await page.evaluate(() => getComputedStyle(document.body).backgroundColor)) === 'rgb(238, 241, 244)',
    'then Alta’s page is given back as it is, its own ground too');
  check(/no part/.test(bad.reason) && bad.shape.buttons.includes('back to top') && Array.isArray(bad.shape.flow) && !JSON.stringify(bad.shape).includes('derivative of f'),
    `and the app is told why, with what the page held — its makeup, its buttons — never the question (“${bad.reason}”)`);
  const nr = (await posts()).length;
  await page.evaluate(() => { window.__flow.appendChild(window.__q); });
  await waitFor((p, k) => k >= nr && p.kind === 'focus' && p.state === 'question', 'the question again');
  check(!(await vis('header')) && (await vis('#asks')), 'when the page is a question again, it is shown alone again');
  // (1.3.70) a loading part (a spinner, "Loading…") in the question's place: out of sight, the app told, until the question
  // is back — never shown in between
  const nld = (await posts()).length;
  await page.evaluate(() => { window.__q.remove(); window.__flow.insertAdjacentHTML('beforeend', '<section id="ldsec" class="compound__compound___x"><div class="theme__card___3E6YM"><div class="MuiCircularProgress-root" style="width:40px;height:40px"></div><p>Loading…</p></div></section>'); });
  const ldh = await waitFor((p, k) => k >= nld && p.kind === 'hidden', 'out of sight while loading', 40);
  await page.waitForTimeout(1200);
  const ldShown = (await posts()).slice(nld).some((p) => p.kind === 'shown');
  check(!!ldh && !ldShown && !(await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-shown'))), 'a loading part is recognised: kept out of sight, never shown');
  const nld2 = (await posts()).length;
  await page.evaluate(() => { document.getElementById('ldsec').remove(); window.__flow.appendChild(window.__q); });
  const ldBack = await waitFor((p, k) => k >= nld2 && p.kind === 'shown', 'the question shown once loaded', 60);
  check(!!ldBack && (await vis('#asks')), 'and the question shown once it is there');

  // Alta opens on the assignment's overview: its objectives read from whatever Alta's page reads
  await page.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a1`);
  const ov = await waitFor((p) => p.kind === 'overview' && p.objectives?.length && p.objectives[0].id === 'lo1', 'overview');
  check(ov.name === 'Differentiation Rules 2' && ov.objectives.length === 3 && ov.objectives[0].name === 'Combine the product and quotient rules' && ov.objectives[0].low === 4 && ov.objectives[0].high === 9 && ov.started === false && ov.percent === 0 && !!ov.due,
    `the overview: the assignment, its objectives with their estimates, not started, when it is due (${ov.objectives.map((o) => o.name).join(' · ')})`);
  check(await page.evaluate(() => window.__simplAlta === undefined && !document.getElementById('simpl-theme')), 'it gives the app no hands in the page and no style of its own');
  const fo = await waitFor((p) => p.kind === 'focus', 'how the overview is shown');
  check(fo.state === 'overview' && !(await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-q'))), 'the overview is recognised and shown as it is (no Error Logs for it)');

  // the student starts it on Alta's own page
  await page.click('#start');
  await page.waitForURL(/\/practice$/);
  const c1 = await waitFor((p) => p.kind === 'content', 'the question’s answer');
  check(c1.name === 'Linear, Polynomial, and Piecewise Functions' && c1.percent === 62 && c1.objectives.length === 3 && c1.targets[1].id === 'lo2' && c1.current.id === 'lo2',
    'it passes the assignment, its objectives, their mastery and the objective on screen');
  check(c1.question === undefined && c1.history === undefined && !JSON.stringify(c1).includes('Which function is linear'), 'and nothing of the question');
  await page.waitForTimeout(1200);
  check(await page.evaluate(() => !!document.getElementById('welcome')), 'Alta’s own pop-ups are left for the student');
  await page.click('#gotit');

  // the student answers on Alta's page: the mastery moves when Alta's answer to the check says so
  await page.click('#o0');
  await page.click('#check');
  const moved = await waitFor((p) => p.kind === 'overview' && p.percent === 66, 'progress after the check');
  check(moved.targets.length === 3 && moved.targets[1].id === 'lo2' && moved.targets[1].progress === 0.59 && moved.progress === 0.66,
    `after a check, the mastery Alta’s answer to it carries is passed: ${moved.percent}%, the objective on screen at ${moved.targets[1].progress}`);
  const bar = await waitFor((p) => p.kind === 'meter' && p.percent === 66, 'Alta’s mastery bar');
  check(bar.bars.some((b) => b.label === 'mastery' && Math.abs(b.value - 0.66) < 1e-9), `and Alta’s own mastery bar is read as it moves (${bar.percent}%)`);
  check(await page.evaluate(() => { const b = document.getElementById('check'); return !document.querySelector('[style*="-10000px"]') && b.style.cssText === '' ; }), 'Alta’s own buttons are left where Alta put them');

  // (1.3.34) a lesson and its question in one answer: the objective is the question's
  const n0 = (await posts()).length;
  const withLesson = await page.evaluate(() => fetch('/mock-alta/api/content?assignmentId=a1&lesson=1').then((r) => r.text()));
  check(withLesson.length > 0, 'a lesson sent with its question is fetched by the page');
  const c2 = await waitFor((p, k) => k >= n0 && p.kind === 'content', 'the lesson’s answer');
  check(!!c2.current.id, `the objective on screen is still read when Alta sends a lesson first (${c2.current.id})`);

  const all = JSON.stringify(await posts());
  const leaked = SECRETS.filter((s) => all.includes(s));
  check(!leaked.length, `never the answer key, the student’s ids or the school’s launch data (${leaked.join(', ') || 'none'})`);

  // (1.3.101 / 0.3.21) the open's speed, checked on each copy of the page script that says how long its part was still
  // (`quiet` in its shown post) — the Mac's here, Windows' below where it is in this checkout. The question in sight as
  // soon as it is drawn: within 400 ms of the learn page's DOMContentLoaded (1.3.100 / 0.3.20 waited for 350 ms with
  // nothing changed anywhere in the page, at least), no cap, each post timed for the app's timeline of the open and the
  // page known as Alta's — the best of three loads, so a slow moment on a shared runner does not decide it. So too while
  // Alta's chrome round the question keeps changing. But never before it is drawn: its answer box and its maths in first.
  const speedChecks = async (label, src, win) => {
    const LEARN = `${BASE}/mock-alta/learn/course/c1/assignment/a3`;
    const open = async (extra) => {
      const sp = await browser.newPage();
      // (each post's moment on the page's own clock too; `__onPost` for a check that looks at the page as it is posted)
      await sp.addInitScript((w) => {
        window.__posted = []; window.__postedAt = [];
        const put = (s) => { window.__posted.push(s); window.__postedAt.push(performance.now()); if (window.__onPost) window.__onPost(JSON.parse(s)); };
        if (w) { window.chrome = window.chrome || {}; window.chrome.webview = { postMessage: put }; } else window.webkit = { messageHandlers: { simplAlta: { postMessage: put } } };
      }, win);
      await sp.addInitScript({ content: src });
      if (extra) await sp.addInitScript(extra);
      return sp;
    };
    const shownOn = async (sp) => {
      await sp.goto(LEARN);
      for (let i = 0; i < 60; i++) {
        const s = await sp.evaluate(() => {
          const n = performance.getEntriesByType('navigation')[0], said = window.__posted.map((x) => JSON.parse(x)), k = said.findIndex((p) => p.kind === 'shown');
          return k < 0 ? null : { after: Math.round(window.__postedAt[k] - n.domContentLoadedEventStart), post: said[k], said };
        });
        if (s) return s;
        await sp.waitForTimeout(100);
      }
      return null;
    };
    const timed = (s) => ['frame', 'focus', 'shown'].every((k) => s.said.some((p) => p.kind === k && p.t > 0 && p.nav > 0));
    const sp = await open();
    const loads = [];
    for (let i = 0; i < 3; i++) loads.push(await shownOn(sp));
    await sp.close();
    const best = Math.min(...loads.map((s) => (s ? s.after : Infinity)));
    check(loads.every((s) => s && s.post.how === 'question' && s.post.capped === false && s.post.quiet >= 100 && timed(s) && s.said[0].kind === 'frame' && s.said[0].alta === true) && best < 400,
      `${label}: the question is shown ${best} ms after the learn page’s DOMContentLoaded (under 400; ${loads.map((s) => s && s.after).join(' / ')}), quiet ${loads.map((s) => s && s.post.quiet).join(' / ')} ms, no cap — its posts timed, its page known as Alta’s`);
    // (and so while Alta's header keeps changing, every 40 ms: only the part shown is waited on, and changes never put off
    // the next look at the page for more than 300 ms — 1.3.100 / 0.3.20 never looked while the page kept changing, and
    // showed it only when its nine-second veil ran out)
    const np = await open(() => { document.addEventListener('DOMContentLoaded', () => { let k = 0; const h = document.querySelector('header'); setInterval(() => { if (h) h.setAttribute('data-k', String(k)); const b = document.createElement('span'); b.textContent = String(k++); (h || document.body).appendChild(b); }, 40); }); });
    const sn = await shownOn(np);
    await np.close();
    check(!!sn && sn.after < 700 && sn.post.how === 'question' && sn.post.capped === false, `${label}: with Alta’s header changing every 40 ms, the question is still shown ${sn && sn.after} ms after DOMContentLoaded (under 700), no cap`);
    // (Learnosity's answer box put in 300 ms after the stem, its empty embed there meanwhile, and a formula of MathJax 2's
    // not drawn until then: the question shown only once both are in, not capped)
    const lp = await open(() => {
      window.__inAtShown = null;
      window.__onPost = (p) => { if (p.kind === 'shown') window.__inAtShown = !!document.querySelector('.lrn_widget') && !!document.getElementById('MathJax-Element-1-Frame'); };
      document.addEventListener('DOMContentLoaded', () => {
        const w = document.querySelector('.lrn_widget'), box = w && w.parentNode, asks = document.getElementById('asks');
        if (w) w.remove();
        const s = document.createElement('script'); s.type = 'math/tex'; s.textContent = 'x^2'; if (asks) asks.appendChild(s);
        setTimeout(() => {
          if (box && w) box.appendChild(w);
          s.id = 'MathJax-Element-1';
          const f = document.createElement('span'); f.id = 'MathJax-Element-1-Frame'; f.className = 'MathJax'; f.textContent = 'x²';
          if (asks) asks.insertBefore(f, s);
        }, 300);
      });
    });
    const sl = await shownOn(lp);
    const inAtShown = await lp.evaluate(() => window.__inAtShown);
    await lp.close();
    check(!!sl && sl.after >= 300 && sl.after < 900 && sl.post.how === 'question' && sl.post.capped === false && inAtShown === true,
      `${label}: with Learnosity’s box and a MathJax formula drawn 300 ms after the stem, the question waits for them: shown ${sl && sl.after} ms after DOMContentLoaded, both in, no cap`);
    // (Learnosity's box put in later still, 1.7 s after the stem — after the second and a half the cap would allow once
    // the box is no longer missing — and what it holds 60 ms after that: its cap never lowered by the box coming in, so
    // the question is still shown on 100 ms of quiet with all of it in, never at once, half drawn)
    const bp = await open(() => {
      window.__boxAtShown = null;
      window.__onPost = (p) => { if (p.kind === 'shown') window.__boxAtShown = !!document.querySelector('.lrn_widget') && !!document.getElementById('late-fill'); };
      document.addEventListener('DOMContentLoaded', () => {
        const w = document.querySelector('.lrn_widget'), box = w && w.parentNode;
        if (w) w.remove();
        setTimeout(() => {
          if (box && w) box.appendChild(w);
          setTimeout(() => { const f = document.createElement('div'); f.id = 'late-fill'; f.textContent = 'answer input'; if (w) w.appendChild(f); }, 60);
        }, 1700);
      });
    });
    const sb = await shownOn(bp);
    const boxAtShown = await bp.evaluate(() => window.__boxAtShown);
    await bp.close();
    check(!!sb && sb.after >= 1700 && sb.after < 2500 && sb.post.how === 'question' && sb.post.capped === false && sb.post.quiet >= 100 && boxAtShown === true,
      `${label}: with Learnosity’s box drawn 1.7 s after the stem, the question still waits for it: shown ${sb && sb.after} ms after DOMContentLoaded, quiet ${sb && sb.post.quiet} ms, the box and what it holds in, no cap`);
  };
  if (/quiet:/.test(source)) await speedChecks('the page script', source, false);

  // (maths drawn whole) Alta's maths drawn whole the first time, and the question never left out of sight — checked on
  // each copy of the page script. Learnosity's answer box (MathQuill) sizes its brackets and its √ to what they hold as
  // it draws them, from their height then, and never again until the student types: the next question, put in as the
  // last went and drawn before the page script had looked at the page again, was drawn hidden (display: none — nothing
  // has a size), its brackets and √ flat, and stayed so, in dark and light and in the browser, until the page was loaded
  // again. Here a box is drawn as MathQuill draws one, from what it measures as it is drawn: in the next question put in
  // at once and a moment later, in dark and light; in a box Alta itself drew out of sight (sized again once it shows,
  // in the browser too); and a question Alta shows by its class alone, a page that never settles, and a look at the page
  // that goes wrong are never left out of sight.
  const renderChecks = async (label, src, win) => {
    const LEARN = `${BASE}/mock-alta/learn/course/c1/assignment/a3`;
    const open = async (dark, extra) => {
      const rp = await browser.newPage();
      await rp.addInitScript(({ w, dark }) => {
        window.__posted = []; window.__postedAt = [];
        const put = (s) => { window.__posted.push(s); window.__postedAt.push(performance.now()); };
        if (w) { window.chrome = window.chrome || {}; window.chrome.webview = { postMessage: put }; } else window.webkit = { messageHandlers: { simplAlta: { postMessage: put } } };
        window.__simplDark = dark; window.__simplPicsLight = false; window.__simplBrowser = false;
        // (MathQuill's own makeup: each bracket and the √ a .mq-scaled, sized by a transform from what it holds, in ems,
        // as it is drawn — its Bracket and SquareRoot reflow)
        window.__drawBox = (into) => {
          into.innerHTML = '<span class="mq-math-mode" style="font-size:20px"><span class="mq-root-block">d/dx <span class="mq-non-leaf"><span class="mq-scaled mq-paren" style="display:inline-block">(</span><span class="mq-non-leaf" style="display:inline-block"><span class="mq-non-leaf"><span class="mq-scaled mq-sqrt-prefix" style="display:inline-block">√</span><span class="mq-non-leaf mq-sqrt-stem" style="display:inline-block;border-top:1px solid;padding-top:2px">64x<sup>-3</sup></span></span></span><span class="mq-scaled mq-paren" style="display:inline-block">)</span></span> = <span class="mq-editable-field" style="display:inline-block;width:80px;height:24px;border:1px solid"></span></span></span>';
          const em = (e, h) => h / parseFloat(getComputedStyle(e).fontSize);
          const stem = into.querySelector('.mq-sqrt-stem'), inner = into.querySelector('.mq-paren').nextElementSibling;
          const hs = em(stem, stem.clientHeight), hb = em(inner, inner.offsetHeight);
          into.querySelector('.mq-sqrt-prefix').style.transform = `scale(1, ${hs - 0.1})`;
          into.querySelectorAll('.mq-paren').forEach((p) => { p.style.transform = `scale(${Math.min(1 + 0.2 * (hb - 1), 1.2)}, ${1.2 * hb})`; });
          const sec = into.closest('section');
          return { h: Math.round(hb * 100) / 100, display: sec ? getComputedStyle(sec).display : '', opacity: sec ? getComputedStyle(sec).opacity : '' };
        };
        // (each bracket's and the √'s height, as drawn now: 0 or less is flat)
        window.__boxScales = (root) => [...(root || document).querySelectorAll('.mq-scaled')].map((e) => { const m = /scale\(([^,]+),\s*([^)]+)\)/.exec(e.style.transform); return m ? Math.round(+m[2] * 100) / 100 : null; });
      }, { w: win, dark });
      await rp.addInitScript({ content: src });
      if (extra) await rp.addInitScript(extra);
      return rp;
    };
    const said = (rp) => rp.evaluate(() => window.__posted.map((s, i) => Object.assign(JSON.parse(s), { at: window.__postedAt[i] })));
    const shownBy = async (rp, after, ms) => {
      for (let i = 0; i < ms / 50; i++) { const s = (await said(rp)).filter((p) => p.kind === 'shown' && p.at >= after); if (s.length) return s[0]; await rp.waitForTimeout(50); }
      return null;
    };
    const ready = async (rp) => { await rp.goto(LEARN); const s = await shownBy(rp, 0, 6000); await rp.waitForTimeout(400); return s; };
    const whole = (sc) => sc.length === 3 && sc.every((v) => v > 0.8);

    // the next question as Alta's React puts it in: a new part where the last was, Learnosity's box drawn into it in the
    // same moment or 60 ms later (before the page script has looked at the page again) — in dark and in light
    for (const dark of [true, false]) {
      for (const D of [-1, 60]) {
        const rp = await open(dark);
        await ready(rp);
        const t0 = await rp.evaluate((D) => {
          const old = document.getElementById('qsec'), sec = old.cloneNode(true);
          sec.id = 'qsec2';
          [sec, ...sec.querySelectorAll('*')].forEach((e) => [...e.attributes].forEach((a) => { if (/^data-simpl/.test(a.name)) e.removeAttribute(a.name); }));
          const w = sec.querySelector('.lrn_widget'), holder = w.parentNode;
          w.remove();
          old.replaceWith(sec);
          window.__drawn = null;
          const draw = () => { const w2 = document.createElement('div'); w2.className = 'lrn lrn_widget lrn_qr lrn_clozeformula'; holder.appendChild(w2); window.__drawn = window.__drawBox(w2); };
          if (D < 0) draw(); else setTimeout(draw, D);
          return performance.now();
        }, D);
        await rp.waitForTimeout(1200);
        const r = await rp.evaluate(() => {
          const s = document.getElementById('qsec2'), cs = getComputedStyle(s);
          return { drawn: window.__drawn, scales: window.__boxScales(s), kept: s.hasAttribute('data-simpl-keep'), opacity: cs.opacity, display: cs.display, filter: cs.filter, q: document.documentElement.hasAttribute('data-simpl-q') };
        });
        const fixed = (await said(rp)).some((p) => p.kind === 'remeasured' && p.at >= t0);
        await rp.close();
        check(!!r.drawn && r.drawn.h > 1 && r.drawn.display !== 'none' && r.drawn.opacity === '0' && whole(r.scales) && r.kept && r.q && r.opacity === '1' && r.display !== 'none' && /invert/.test(r.filter) === dark && !fixed,
          `${label}, ${dark ? 'dark' : 'light'}: the next question’s answer box drawn ${D < 0 ? 'in the moment' : D + ' ms after'} its part is put in is drawn whole — laid out as it was drawn, out of sight but never hidden (${r.drawn && r.drawn.h} em tall, opacity ${r.drawn && r.drawn.opacity}, display ${r.drawn && r.drawn.display}), its brackets and √ ${JSON.stringify(r.scales)}, the part kept and in sight after`);
      }
    }

    // a box Alta itself drew out of sight (its own display: none), flat, then shown: sized again by the page script as
    // MathQuill sizes it, and the app told — on the question shown alone, and in the browser (Alta's page as it is)
    for (const inBrowser of [false, true]) {
      const rp = await open(true);
      await ready(rp);
      if (inBrowser) { await rp.evaluate(() => { window.__simplBrowser = true; window.__simplBrowserNow(); }); await rp.waitForTimeout(400); }
      const t0 = await rp.evaluate(() => {
        const box = document.createElement('div'); box.id = 'altaHid'; box.style.display = 'none';
        document.querySelector('#qsec .lrn_widget').appendChild(box);
        window.__flat = window.__drawBox(box);
        window.__flatScales = window.__boxScales(box);
        box.style.display = '';
        return performance.now();
      });
      await rp.waitForTimeout(900);
      const r = await rp.evaluate(() => ({ before: window.__flatScales, after: window.__boxScales(document.getElementById('altaHid')), q: document.documentElement.hasAttribute('data-simpl-q') }));
      const told = (await said(rp)).find((p) => p.kind === 'remeasured' && p.at >= t0);
      await rp.close();
      check(r.before.every((v) => v <= 0) && whole(r.after) && !!told && told.count === 3 && r.q === !inBrowser,
        `${label}${inBrowser ? ', in the browser' : ''}: a box Alta drew out of sight, its brackets and √ flat (${JSON.stringify(r.before)}), is sized again once it shows (${JSON.stringify(r.after)}), and the app told (${told && told.count})`);
    }

    // the next question shown by its class alone (no part put in or taken out): seen as it comes — before, it waited out
    // the eight seconds' grace between parts, or for good while the last one was still kept
    {
      const rp = await open(true);
      await ready(rp);
      await rp.evaluate(() => {
        const st = document.createElement('style'); st.textContent = '.alta-gone { display: none; }'; document.head.appendChild(st);
        const old = document.getElementById('qsec'), sec = old.cloneNode(true);
        sec.id = 'qsec2'; sec.classList.add('alta-gone');
        [sec, ...sec.querySelectorAll('*')].forEach((e) => [...e.attributes].forEach((a) => { if (/^data-simpl/.test(a.name)) e.removeAttribute(a.name); }));
        old.after(sec);
      });
      await rp.waitForTimeout(800);
      const t0 = await rp.evaluate(() => { document.getElementById('qsec').classList.add('alta-gone'); setTimeout(() => document.getElementById('qsec2').classList.remove('alta-gone'), 300); return performance.now(); });
      const s = await shownBy(rp, t0, 4000);
      const r = await rp.evaluate(() => { const q2 = document.getElementById('qsec2'); return { kept: q2.hasAttribute('data-simpl-keep'), opacity: getComputedStyle(q2).opacity }; });
      await rp.close();
      check(!!s && s.at - t0 < 1200 && s.how === 'question' && r.kept && r.opacity === '1',
        `${label}: the next question, shown by its class alone, is in sight ${s ? Math.round(s.at - t0) : '—'} ms after the last went (under 1200)`);
    }

    // the first question of a page, shown by its class alone 300 ms in
    {
      const rp = await open(true, () => { document.addEventListener('DOMContentLoaded', () => { const st = document.createElement('style'); st.textContent = '.alta-gone { display: none; }'; document.head.appendChild(st); const q = document.getElementById('qsec'); q.classList.add('alta-gone'); setTimeout(() => q.classList.remove('alta-gone'), 300); }); });
      await rp.goto(LEARN);
      const s = await shownBy(rp, 0, 9500);
      const dcl = await rp.evaluate(() => performance.getEntriesByType('navigation')[0].domContentLoadedEventStart);
      await rp.close();
      check(!!s && s.how === 'question' && s.at - dcl < 1500, `${label}: a first question shown by its class alone, 300 ms in, is in sight ${s ? Math.round(s.at - dcl) : '—'} ms after DOMContentLoaded (under 1500; it waited eight seconds before)`);
    }

    // a page that never settles — its answer box never drawn (Learnosity's empty embed only), the part changing all the
    // time — is still shown, at its cap, never left behind the skeleton
    {
      const rp = await open(true, () => { document.addEventListener('DOMContentLoaded', () => { const w = document.querySelector('.lrn_widget'); const holder = w.parentNode; w.remove(); holder.insertAdjacentHTML('beforeend', '<div class="learnosity-item"></div>'); const a = document.getElementById('asks'); let k = 0; setInterval(() => { a.setAttribute('data-k', String(k)); a.lastChild.textContent = 'Find the derivative. ' + (k++ % 2 ? ' ' : ''); }, 30); }); });
      await rp.goto(LEARN);
      const s = await shownBy(rp, 0, 6000);
      const dcl = await rp.evaluate(() => performance.getEntriesByType('navigation')[0].domContentLoadedEventStart);
      await rp.close();
      check(!!s && s.how === 'question' && s.capped === true && s.at - dcl < 3500, `${label}: a page that never settles (no answer box, changing all the time) is still shown at its cap, ${s ? Math.round(s.at - dcl) : '—'} ms after DOMContentLoaded (under 3500)`);
    }

    // a look at the page that goes wrong (here, Alta's page throwing as it is read, twice) as the next question is shown by
    // its class: the page is looked at again on its own and the question shown — what Light then Dark did by hand
    {
      const rp = await open(true);
      await ready(rp);
      await rp.evaluate(() => {
        const st = document.createElement('style'); st.textContent = '.alta-gone { display: none; }'; document.head.appendChild(st);
        const old = document.getElementById('qsec'), sec = old.cloneNode(true);
        sec.id = 'qsec2'; sec.classList.add('alta-gone');
        [sec, ...sec.querySelectorAll('*')].forEach((e) => [...e.attributes].forEach((a) => { if (/^data-simpl/.test(a.name)) e.removeAttribute(a.name); }));
        old.after(sec);
        const qsa = Document.prototype.querySelectorAll;
        window.__throwReads = 0;
        document.querySelectorAll = function (sel) { if (window.__throwReads > 0 && sel === '[class*="learn-flow-body"]') { window.__throwReads--; throw new Error('unreadable'); } return qsa.call(this, sel); };
      });
      await rp.waitForTimeout(800);
      const t0 = await rp.evaluate(() => { window.__throwReads = 2; document.getElementById('qsec').classList.add('alta-gone'); setTimeout(() => document.getElementById('qsec2').classList.remove('alta-gone'), 50); return performance.now(); });
      let keptAt = null;
      for (let i = 0; i < 60 && keptAt === null; i++) { keptAt = await rp.evaluate((t0) => (document.getElementById('qsec2').hasAttribute('data-simpl-keep') ? Math.round(performance.now() - t0) : null), t0); if (keptAt === null) await rp.waitForTimeout(50); }
      const r = await rp.evaluate(() => { const q2 = document.getElementById('qsec2'); return { left: window.__throwReads, opacity: getComputedStyle(q2).opacity, shown: document.documentElement.hasAttribute('data-simpl-shown') }; });
      await rp.close();
      check(r.left === 0 && keptAt !== null && keptAt < 2000 && r.opacity === '1' && r.shown, `${label}: a look at the page that goes wrong (twice) never leaves the next question out of sight: it is looked at again on its own and shown, ${keptAt} ms after the last went (under 2000)`);
    }

    // (maths drawn whole) a part Alta puts in above the question (a lesson — More Instruction): while it is not kept yet,
    // see-through, it is out of the flow, so the question never moves down under its glass with a gap over it; the two
    // move together once it is kept, in the same frame the lesson comes into sight (as they did when it was hidden)
    {
      const rp = await open(true);
      await ready(rp);
      const r = await rp.evaluate(() => new Promise((done) => {
        const q = document.getElementById('qsec'), top0 = q.getBoundingClientRect().top, t0 = performance.now(), frames = [];
        window.mockLesson();
        const l = document.getElementById('lsec');
        const step = () => {
          const cs = getComputedStyle(l), kept = l.hasAttribute('data-simpl-keep');
          frames.push({ kept, top: Math.round(q.getBoundingClientRect().top), opacity: cs.opacity, display: cs.display, h: Math.round(l.getBoundingClientRect().height) });
          if (kept || performance.now() - t0 > 2000) done({ top0: Math.round(top0), frames }); else requestAnimationFrame(step);
        };
        requestAnimationFrame(step);
      }));
      await rp.waitForTimeout(300);
      const after = await rp.evaluate(() => ({ top: Math.round(document.getElementById('qsec').getBoundingClientRect().top), opacity: getComputedStyle(document.getElementById('lsec')).opacity }));
      await rp.close();
      const unkept = r.frames.filter((f) => !f.kept), moved = unkept.filter((f) => Math.abs(f.top - r.top0) > 1);
      check(unkept.length > 0 && moved.length === 0 && unkept.every((f) => f.display !== 'none' && f.opacity === '0' && f.h > 0) && r.frames[r.frames.length - 1].kept && after.top > r.top0 + 40 && after.opacity === '1',
        `${label}: a lesson put in above the question never moves it while the lesson is still out of sight (${unkept.length} frames, ${moved.length} with the question moved; the lesson laid out, ${unkept.length ? unkept[0].h : '—'} px tall), and both move together once it is kept (question ${r.top0} → ${after.top} px)`);
    }

    // (maths drawn whole) the app's last resort, its skeleton still up 26 seconds on: Alta's page shown as it is by the page
    // script itself — the focus lifted, the app told why and that it is shown — and what the page script knows kept in
    // step: the next part is laid out again, and the page can still be put out of sight between parts after
    {
      const rp = await open(true);
      await ready(rp);
      const n0 = (await said(rp)).length;
      await rp.evaluate(() => { window.__qsec = document.getElementById('qsec'); window.__qsec.remove(); });
      let hid = false;
      for (let i = 0; i < 40 && !hid; i++) { hid = (await said(rp)).slice(n0).some((p) => p.kind === 'hidden'); if (!hid) await rp.waitForTimeout(50); }
      const n1 = (await said(rp)).length;
      const back = await rp.evaluate(() => (window.__simplAsIsNow ? window.__simplAsIsNow() : null));
      const st = await rp.evaluate(() => ({ q: document.documentElement.hasAttribute('data-simpl-q'), shown: document.documentElement.hasAttribute('data-simpl-shown'), header: (() => { const h = document.querySelector('header'); const b = h && h.getBoundingClientRect(); return !!b && b.width > 0 && b.height > 0; })() }));
      const p1 = (await said(rp)).slice(n1);
      const why = p1.find((p) => p.kind === 'focus' && p.state === 'unknown'), shownAsIs = p1.find((p) => p.kind === 'shown' && p.how === 'as it is');
      await rp.evaluate(() => document.querySelector('[class*="learn-flow-body"]').appendChild(window.__qsec));
      let kept = false;
      for (let i = 0; i < 40 && !kept; i++) { kept = await rp.evaluate(() => window.__qsec.hasAttribute('data-simpl-keep') && document.documentElement.hasAttribute('data-simpl-q')); if (!kept) await rp.waitForTimeout(50); }
      await rp.waitForTimeout(300);
      const n2 = (await said(rp)).length;
      await rp.evaluate(() => window.__qsec.remove());
      let hidAgain = false;
      for (let i = 0; i < 40 && !hidAgain; i++) { hidAgain = (await said(rp)).slice(n2).some((p) => p.kind === 'hidden'); if (!hidAgain) await rp.waitForTimeout(50); }
      await rp.close();
      check(hid && back === true && !st.q && st.shown && st.header && !!why && /26 seconds/.test(why.reason) && !!why.shape && !!shownAsIs && kept && hidAgain,
        `${label}: the app’s last resort shows Alta’s page as it is through the page script (${back}): the focus lifted, the app told why (“${why && why.reason}”) and that it is shown; the next part laid out again (${kept}), and put out of sight between parts after (${hidAgain})`);
    }
  };

  // (Windows 0.3.5) Simpl for Windows' copy of the page script (windows/SimplFluent/Screens/Alta/AltaHook.cs), where it is
  // in this checkout: Alta's pop-ups read whole once they have settled — the body that comes in after the pop-up opens,
  // its lists, formulas and pictures, all its buttons, each step — and given back to the page when they cannot be
  // read. Before, the body was read the moment the pop-up was seen and pictured 380 ms later, often still empty.
  const winHook = join(ROOT, 'windows/SimplFluent/Screens/Alta/AltaHook.cs');
  if (existsSync(winHook)) {
    console.log('\nSimpl for Windows’ copy of the page script, on the mock’s pop-ups');
    const cs = readFileSync(winHook, 'utf8');
    const w = /public const string Source = """\n([\s\S]*?)\n\s*""";/.exec(cs);
    check(!!w, 'the Windows page script is found in AltaHook.cs');
    const winSource = w ? w[1].split('\n').map((l) => l.replace(/^ {8}/, '')).join('\n') : '';
    const wp = await browser.newPage();
    await wp.addInitScript(() => { window.__posted = []; window.chrome = window.chrome || {}; window.chrome.webview = { postMessage: (s) => window.__posted.push(s) }; });
    await wp.addInitScript({ content: winSource });
    const wposts = async () => (await wp.evaluate(() => window.__posted.slice())).map((s) => JSON.parse(s));
    const wwait = async (test, what, tries = 80) => {
      for (let i = 0; i < tries; i++) { const p = (await wposts()).filter(test); if (p.length) return p[p.length - 1]; await wp.waitForTimeout(100); }
      throw new Error(`no ${what} came`);
    };
    const full = (p) => p.kind === 'dialog' && p.open === true && !p.pending;
    const hiddenIn = (sel) => wp.$eval(sel, (e) => getComputedStyle(e).visibility === 'hidden').catch(() => null);
    const pngSize = (src) => { const b = Buffer.from(String(src).split(',')[1] || '', 'base64'); return b.length > 24 && b.toString('ascii', 1, 4) === 'PNG' ? [b.readUInt32BE(16), b.readUInt32BE(20)] : null; };
    await wp.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a3?sample=dialog`);
    // a pop-up as Alta's (Material's, its Next and Not now in capitals by a style), a spinner in its body for a second
    // and a half before its words come
    await wwait((p) => p.kind === 'dialog' && p.pending, 'word of a pop-up');
    const seenAt = Date.now();
    check(await hiddenIn('#rich .MuiDialog-paper'), 'a pop-up of Alta’s is out of sight in the page as soon as it is seen (the app shows it as its own, or gives it back)');
    await wp.waitForTimeout(900);
    check(!(await wposts()).some(full), 'and it is not passed while its body is still loading (a spinner in it)');
    const d1 = await wwait(full, 'the pop-up, read whole', 60);
    const heldFor = Date.now() - seenAt;
    const imgs = [...d1.html.matchAll(/<img src="(data:image\/png;base64,[^"]+)"[^>]*>/g)].map((m) => m[1]);
    check(d1.title === 'Knewton Alta' && !d1.html.includes('Knewton Alta') && heldFor >= 900,
      `it is passed once its body is in and has settled (${heldFor} ms after it was seen, ${d1.ready}), titled by its heading, which is not repeated in its body`);
    check(d1.html.includes('<b>adaptive practice</b>') && /<ul>(?:<li>[\s\S]*?<\/li>){3}<\/ul>/.test(d1.html) && d1.words >= 40 && !/spinner|progressbar/i.test(d1.html),
      `its body whole as plain HTML for the app to draw: its words (${d1.words}), bold as Alta has it, its list of three`);
    check(d1.maths === 2 && d1.html.includes('data-equation-content="p \\geq 0.9"') && d1.html.includes('<p style="text-align:center"><img class="equation_image" data-equation-content="\\frac{d}{dx}\\ln x = \\frac{1}{x}">'),
      `its formulas as their TeX — KaTeX's from its annotation, MathJax's not drawn yet from its script, set on a line of its own (${d1.maths})`);
    const sizes = imgs.map(pngSize);
    check(d1.pics === 2 && d1.missed === 0 && imgs.length === 2 && JSON.stringify(sizes) === JSON.stringify([[480, 180], [400, 160]]) && /alt="Mastery by objective"/.test(d1.html),
      `its drawing and its picture made into pictures here, at twice their size (${JSON.stringify(sizes)})`);
    check(d1.html.includes(`<a href="${BASE}/mock-alta/help/mastery">how mastery works</a>`), 'its link, by its whole address');
    check(JSON.stringify(d1.buttons) === JSON.stringify([{ label: 'Not now', primary: false, enabled: true, close: false }, { label: 'Next', primary: true, enabled: true, close: false }]) && !/Not now|NEXT|>Next</.test(d1.html),
      `every button, in its order, in Alta's own words (not a style's capitals), Alta's main one told (${JSON.stringify(d1.buttons.map((b) => b.label))}), none in its body`);
    // (the app shows it as its own: hidden in the page; a change to it read with its words' breaks — before, a pop-up read
    // while hidden lost them, "Knewton AltaGOT IT")
    await wp.evaluate(() => window.__simplDialog.hide());
    const n1 = (await wposts()).length;
    await wp.evaluate(() => document.getElementById('rich-body').insertAdjacentHTML('beforeend', '<p>One more line of it.</p>'));
    const d1b = await wwait((p, k) => k >= n1 && full(p), 'the pop-up changed while the app shows it', 40).catch(() => null) || (await wposts()).slice(n1).filter(full).pop();
    check(!!d1b && d1b.html.includes('<p>One more line of it.</p>') && d1b.words === d1.words + 5 && (await hiddenIn('#rich .MuiDialog-paper')),
      'a change to it while the app shows it is read whole, its words apart, the pop-up still out of sight in the page');
    await wp.evaluate(() => { document.querySelector('#rich a').addEventListener('click', (e) => { e.preventDefault(); window.__followed = e.currentTarget.getAttribute('href'); }); });
    check((await wp.evaluate((h) => window.__simplDialog.link(h), `${BASE}/mock-alta/help/mastery`)) && (await wp.evaluate(() => window.__followed)) === '/mock-alta/help/mastery',
      'a link in its body is followed as Alta’s page follows it (its own link pressed)');
    // its next step: Next pressed from the app
    const n2 = (await wposts()).length;
    check(await wp.evaluate(() => window.__simplDialog.press(1)), 'the app’s Next presses Alta’s');
    const d2 = await wwait((p, k) => k >= n2 && full(p) && /That is all/.test(p.html), 'the pop-up’s second step', 40);
    check(JSON.stringify(d2.buttons.map((b) => [b.label, b.primary])) === JSON.stringify([['Back', false], ['Got it', true]]) && d2.html === '<div><p>That is all. Your mastery of each objective shows beside the question at any time.</p></div>',
      `its next step is passed whole, with its own buttons (${JSON.stringify(d2.buttons.map((b) => b.label))})`);
    const n3 = (await wposts()).length;
    check(await wp.evaluate(() => window.__simplDialog.press(1)), 'the app’s Got it presses Alta’s');
    await wwait((p, k) => k >= n3 && p.kind === 'dialog' && p.open === false, 'the pop-up gone');
    check(!(await wp.$('#rich')) && !(await wp.evaluate(() => document.documentElement.hasAttribute('data-simpl-native-dialog'))), 'when it closes, the app is told and the page left as it was');
    // a pop-up with what the app cannot draw (a form): passed at once, said so — the app gives it back, shown in the page
    const n4 = (await wposts()).length;
    await wp.evaluate(() => { const d = document.createElement('div'); d.id = 'formdlg'; d.innerHTML = '<div role="dialog" aria-label="Report a problem" style="position:fixed;top:20%;left:30%;width:380px;background:#fff;padding:20px"><p>What went wrong?</p><textarea></textarea><button>Send</button></div>'; document.body.appendChild(d); });
    const d3 = await wwait((p, k) => k >= n4 && full(p), 'the pop-up with a form', 40);
    check(d3.live === 'a form' && d3.title === 'Report a problem', `a pop-up the app cannot draw is said to be one (${d3.live}): the app gives it back`);
    await wp.evaluate(() => window.__simplDialog.giveBack());
    check((await hiddenIn('#formdlg [role=dialog]')) === false && (await wp.evaluate(() => document.documentElement.hasAttribute('data-simpl-shown'))),
      'given back, it is shown in the page as Alta draws it, the page in sight under it');
    const n5 = (await wposts()).length;
    await wp.evaluate(() => { document.querySelector('#formdlg p').textContent = 'What went wrong, exactly?'; });
    await wp.waitForTimeout(1200);
    check(!(await wposts()).slice(n5).some(full), 'and left to the page until it closes, whatever it says next');
    await wp.evaluate(() => document.getElementById('formdlg').remove());
    await wwait((p, k) => k >= n5 && p.kind === 'dialog' && p.open === false, 'the form’s pop-up gone');
    // a pop-up in a web component's own tree
    const n6 = (await wposts()).length;
    await wp.evaluate(() => { customElements.define('alta-tip', class extends HTMLElement { connectedCallback() { const r = this.attachShadow({ mode: 'open' }); r.innerHTML = '<div role="dialog" aria-label="Tip" style="position:fixed;top:25%;left:30%;width:360px;background:#fff;padding:20px"><p>Words kept in its own tree.</p><button>OK</button></div>'; } }); document.body.appendChild(document.createElement('alta-tip')); });
    const d4 = await wwait((p, k) => k >= n6 && full(p), 'the pop-up in a web component', 60);
    check(d4.title === 'Tip' && d4.html === '<p>Words kept in its own tree.</p>' && d4.buttons.map((b) => b.label).join() === 'OK', 'a pop-up in a web component’s own tree is read whole too');
    await wp.evaluate(() => document.querySelector('alta-tip').remove());
    await wwait((p, k) => k >= n6 && p.kind === 'dialog' && p.open === false, 'the web component’s pop-up gone');
    // (0.3.5) Show browser: Alta's page as it is, as in a browser — nothing laid out or hidden, its pop-ups its own — and
    // laid out again when it is put away
    const n8 = (await wposts()).length;
    await wp.evaluate(() => { window.__simplBrowser = true; window.__simplBrowserNow(); });
    await wp.waitForTimeout(800);
    const wvis = async (sel) => wp.$eval(sel, (e) => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0 && getComputedStyle(e).visibility !== 'hidden'; }).catch(() => false);
    const asItIs = await wp.evaluate(() => !document.documentElement.hasAttribute('data-simpl-q') && document.documentElement.hasAttribute('data-simpl-shown'));
    const lastOf = (kind) => wposts().then((ps) => ps.slice(n8).filter((p) => p.kind === kind).pop());
    check(asItIs && (await wvis('h1')) && (await wvis('.jss179')) && (await wvis('#submit')) && (await lastOf('buttons'))?.items.length === 0 && (await lastOf('panels'))?.panels.length === 0,
      'with the browser asked for, Alta’s page is shown as it is — its title, objective card and its own buttons — and the app is given no buttons or panels');
    await wp.evaluate(() => window.mockRichDialog(0));
    await wp.waitForTimeout(1500);
    check((await wvis('#rich .MuiDialog-paper')) && !(await wposts()).slice(n8).some((p) => p.kind === 'dialog'), 'and a pop-up of Alta’s stays Alta’s own, in its page, never passed to the app');
    const n9 = (await wposts()).length;
    await wp.evaluate(() => { window.__simplBrowser = false; window.__simplBrowserNow(); });
    const back = await wwait((p, k) => k >= n9 && full(p), 'the pop-up taken up again once the browser is put away', 60);
    check(back.title === 'Knewton Alta' && (await hiddenIn('#rich .MuiDialog-paper')) && !(await wvis('h1')) && (await wp.evaluate(() => document.documentElement.hasAttribute('data-simpl-q'))),
      'put away, the question is laid out alone again and the open pop-up is the app’s again');
    await wp.evaluate(() => { window.__simplBrowser = true; window.__simplBrowserNow(); });
    await wwait((p, k) => k >= n9 && p.kind === 'dialog' && p.open === false, 'the pop-up given back when the browser is asked for again', 30);
    check((await wvis('#rich .MuiDialog-paper')) && !(await wp.evaluate(() => document.documentElement.hasAttribute('data-simpl-native-dialog'))),
      'and asked for again with a pop-up up, the pop-up is given back to the page and the app told it is gone from it');
    await wp.evaluate(() => { document.getElementById('rich').remove(); window.__simplBrowser = false; window.__simplBrowserNow(); });
    // (a page opened while the browser is asked for: in sight at once, never veiled)
    const bp = await browser.newPage();
    await bp.addInitScript(() => { window.__posted = []; window.chrome = window.chrome || {}; window.chrome.webview = { postMessage: (s) => window.__posted.push(s) }; window.__simplBrowser = true; });
    await bp.addInitScript({ content: winSource });
    await bp.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a3`);
    await bp.waitForTimeout(1500);
    const bposts = (await bp.evaluate(() => window.__posted.slice())).map((s) => JSON.parse(s));
    check(bposts.some((p) => p.kind === 'shown' && p.how === 'browser') && !bposts.some((p) => p.kind === 'hidden' || p.kind === 'focus') && (await bp.evaluate(() => getComputedStyle(document.body).opacity)) === '1',
      'a page opened while the browser is asked for is in sight at once, as it is, never veiled');
    await bp.close();
    // a pop-up with nothing in its body that can be read: waited for longer, then passed empty — the app gives it back
    // rather than show an empty dialog
    const n7 = (await wposts()).length;
    const t7 = Date.now();
    await wp.evaluate(() => { const d = document.createElement('div'); d.id = 'emptydlg'; d.innerHTML = '<div role="dialog" aria-label="Knewton Alta" style="position:fixed;top:20%;left:30%;width:380px;height:160px;background:#fff"><button>Got it</button></div>'; document.body.appendChild(d); });
    const d5 = await wwait((p, k) => k >= n7 && full(p), 'the empty pop-up', 90);
    check(d5.words === 0 && d5.maths === 0 && d5.pics === 0 && Date.now() - t7 >= 4500,
      `a pop-up whose body says nothing is waited for (${Date.now() - t7} ms), then passed as empty, for the app to give back to the page`);
    await wp.evaluate(() => document.getElementById('emptydlg').remove());
    // a pop-up the page script cannot read (here, a part of it that throws as it is read): given back to the page at once,
    // never left out of sight, the app told why
    const n10 = (await wposts()).length;
    await wp.evaluate(() => { const d = document.createElement('div'); d.id = 'baddlg'; d.innerHTML = '<div role="dialog" aria-label="Odd" style="position:fixed;top:20%;left:30%;width:380px;background:#fff;padding:20px"><p id="odd">Some words.</p><button>OK</button></div>'; document.body.appendChild(d); document.getElementById('odd').matches = () => { throw new Error('unreadable'); }; });
    const d6 = await wwait((p, k) => k >= n10 && full(p), 'the pop-up that cannot be read', 40);
    check(/could not read \(unreadable\)/.test(d6.live) && (await hiddenIn('#baddlg [role=dialog]')) === false,
      `a pop-up the page script cannot read is given back to the page at once, never left out of sight (${d6.live})`);
    await wp.evaluate(() => document.getElementById('baddlg').remove());
    await wwait((p, k) => k >= n10 && p.kind === 'dialog' && p.open === false, 'the unreadable pop-up gone');
    // (0.3.19) Alta's verdict on the question, told with the panels for the popup's pill: the page opened answered
    // (?sample=wrong), then the strip's words changed, then the strip gone
    await wp.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a3?sample=wrong`);
    const wv = await wwait((p) => p.kind === 'panels' && p.panels.length === 2, 'the answered page’s panels');
    check(wv.verdict === 'wrong', `a page opened answered tells its verdict with its first panels (${wv.verdict})`);
    const nwv = (await wposts()).length;
    await wp.evaluate(() => { document.getElementById('verdict').textContent = 'Yes that’s right. Keep it up!'; });
    check(!!(await wwait((p, k) => k >= nwv && p.kind === 'panels' && p.verdict === 'right', 'the verdict (right)')), 'Alta’s “Yes that’s right” strip is read as a right answer');
    // (and when the strip goes, so does the verdict — then the strip put back, still right, for the next step)
    const nwv2 = (await wposts()).length;
    await wp.evaluate(() => { const v = document.getElementById('verdict'); window.__verdictAt = [v, v.parentNode, v.nextSibling]; v.remove(); });
    check(!!(await wwait((p, k) => k >= nwv2 && p.kind === 'panels' && p.verdict === '', 'the verdict gone')), 'and when the strip goes, so does the verdict');
    const nwv2b = (await wposts()).length;
    await wp.evaluate(() => { const [v, at, next] = window.__verdictAt; at.insertBefore(v, next); });
    await wwait((p, k) => k >= nwv2b && p.kind === 'panels' && p.verdict === 'right', 'the verdict back');
    // (0.3.20) what showed Correct on a question not yet answered: a box whose class says "right" or "status" (layout),
    // and a screen reader's 1-pixel live region keeping "Yes that's right" from the last question — neither tells one
    const nwv3 = (await wposts()).length;
    await wp.evaluate(() => {
      const v = document.getElementById('verdict'); v.className = 'status__bar text-right feedback__incorrect___3bY7t'; v.textContent = '1 of 3';
      v.insertAdjacentHTML('afterend', '<div id="srlive" role="status" aria-live="polite" style="position:absolute;width:1px;height:1px;overflow:hidden;clip:rect(0 0 0 0)">Yes that’s right. Keep it up!</div>');
    });
    check(!!(await wwait((p, k) => k >= nwv3 && p.kind === 'panels' && p.verdict === '', 'no verdict by class')), 'a layout class, a class saying incorrect with no such words, or a screen reader’s hidden live region never shows a verdict');
    await wp.evaluate(() => { document.getElementById('srlive').remove(); document.getElementById('verdict').remove(); });
    await wp.waitForTimeout(500);
    check(!(await wposts()).slice(nwv3).some((p) => p.kind === 'panels' && p.verdict), 'and none is told after it either');
    await wp.close();
    if (/quiet:/.test(winSource)) await speedChecks('Windows’ copy', winSource, true);
    else check(false, 'Windows’ copy of the page script times its words and says how long its part was still (its shown word has `quiet`)');
    await renderChecks('Windows’ copy', winSource, true);
    // (1.3.102 / 0.3.22, on Windows' copy: the Mac's in this checkout is older, and does not say which page is Alta's)
    // the launch as Canvas makes it: the address Canvas hands over is a page of its own, whose form sends itself on to
    // Alta (POSTed), which sends the browser on to the assignment's page. On the way, the page script says the first page
    // is not Alta's (the app keeps its skeleton up over it); then Alta's page is known as Alta's, kept out of sight, and
    // shown laid out — here its overview
    {
      const launch = JSON.parse((await (await fetch(`${BASE}/api/v1/courses/104/external_tools/sessionless_launch?launch_type=assessment&assignment_id=4003`)).text()).replace(/^while\(1\);/, ''));
      const hp = await browser.newPage();
      const hops = [], navs = [];
      await hp.exposeFunction('__simplKeep', (x) => { hops.push(JSON.parse(x)); });
      await hp.addInitScript(() => { window.chrome = window.chrome || {}; window.chrome.webview = { postMessage: (x) => window.__simplKeep(x) }; });
      await hp.addInitScript({ content: winSource });
      hp.on('request', (r) => { if (r.isNavigationRequest() && r.frame() === hp.mainFrame()) navs.push(`${r.method()} ${new URL(r.url()).pathname}`); });
      await hp.goto(launch.url);
      for (let i = 0; i < 80 && !hops.some((p) => p.kind === 'shown' && p.how === 'overview'); i++) await hp.waitForTimeout(100);
      const frames = hops.filter((p) => p.kind === 'frame');
      const altaAt = hops.findIndex((p) => p.kind === 'frame' && p.alta);
      const lastShown = hops.filter((p) => p.kind === 'shown').pop();
      check(/^\/courses\/104\/external_tools\/sessionless_launch$/.test(new URL(launch.url).pathname)
        && JSON.stringify(navs) === JSON.stringify(['GET /courses/104/external_tools/sessionless_launch', 'POST /mock-alta/lti/launch', 'GET /mock-alta/learn/course/c1/assignment/a1'])
        && hp.url().endsWith('/mock-alta/learn/course/c1/assignment/a1'),
        `Windows’ copy: the mock's launch goes as Canvas's does: its own page, its form POSTed to Alta, Alta's page (${navs.join(' → ')})`);
      // (the first page's own `shown`, posted as its form goes, may be lost with it: only never another before Alta's page)
      check(frames.length >= 2 && frames[0].alta === false && altaAt > 0 && hops.slice(0, altaAt).every((p) => p.kind !== 'shown' || p.how === 'not alta') && !!lastShown && lastShown.how === 'overview' && hops.indexOf(lastShown) > altaAt,
        `Windows’ copy: on the way, Canvas's page is said not to be Alta's and Alta's page is, and is shown laid out (${hops.filter((p) => p.kind === 'frame' || p.kind === 'shown').map((p) => p.kind === 'frame' ? `frame${p.alta ? ' alta' : ''}` : `shown ${p.how}`).join(' → ')})`);
      await hp.close();
    }
  }
} finally {
  await browser.close();
  stop();
}

console.log(failed ? `\n${failed} failed` : '\nAll checks passed.');
process.exit(failed ? 1 : 0);

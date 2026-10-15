// Simpl for Mac: the Knewton Alta popup's page script (mac/Simpl/Engine/AltaSession.swift, AltaHook.source).
// First in a stand-in page: it does nothing on any site but Alta's. Then in Chromium on the mock's Alta player
// (scripts/dev/mock-canvas.mjs, /mock-alta/…): it passes the app the assignment, its objectives and their mastery —
// from Alta's own answers, the overview's words and Alta's mastery bars — never the question, the answer key or who
// the student is. (1.3.45) It only reads: the page is worked by the student, never answered, pressed or restyled.
//   node scripts/dev/alta-test.mjs
import { readFileSync } from 'node:fs';
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
const mock = spawn(process.execPath, [join(ROOT, 'scripts/dev/mock-canvas.mjs'), String(PORT), String(SIM)], { stdio: 'ignore' });
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
  await page.evaluate(() => { document.querySelector('section[data-simpl-keep]').insertAdjacentHTML('beforeend', '<div class="lrn_graphplotting"><div class="jxgbox" style="width:300px;height:200px"><svg width="300" height="200"></svg><div class="JXGtext">(2, 0)</div></div></div><svg id="plot" width="240" height="160"></svg><svg id="icon" width="16" height="16"></svg><img id="gimg" alt="" style="display:block;width:220px;height:150px"><svg id="big" width="640" height="420"></svg>'); });
  await page.waitForTimeout(600);
  check(await page.evaluate(() => { const g = document.querySelector('.lrn_graphplotting'); return g.hasAttribute('data-simpl-pic') && !g.querySelector('[data-simpl-pic]') && document.getElementById('plot').hasAttribute('data-simpl-pic') && document.getElementById('gimg').hasAttribute('data-simpl-pic') && !document.getElementById('icon').hasAttribute('data-simpl-pic'); }),
    'a graph’s box (with its labels) and a large drawing are marked to keep their own colours in dark mode; an icon is not');
  // (1.3.70) a large graph in what the question asks is drawn small (300 × 220 at most), its place told to the app for the
  // button by it, and a click on it asks the app to open it large; its rect and zoom are there for the picture
  const nm = (await posts()).length;
  const mini = await page.evaluate(() => { const b = document.getElementById('big'); const r = b.getBoundingClientRect(); return { on: b.hasAttribute('data-simpl-mini'), w: r.width, h: r.height, z: b.style.getPropertyValue('--simpl-zoom') }; });
  const pm = await waitFor((p, k) => k >= nm && p.kind === 'panels' && Array.isArray(p.pics) && p.pics.length === 1, 'the small graph’s place', 30).catch(() => null) || (await posts()).filter((p) => p.kind === 'panels').pop();
  check(mini.on && mini.w <= 302 && mini.h <= 222 && +mini.z < 1 && pm?.pics?.length === 1 && Math.abs(pm.pics[0].w - mini.w) < 2,
    `a large graph is drawn small, its place told to the app (${JSON.stringify({ mini, pics: pm?.pics })})`);
  await page.click('#big');
  const po = await waitFor((p) => p.kind === 'picOpen', 'the graph asked open', 20);
  const pr = await page.evaluate(() => window.__simplPic.rect(0));
  check(po.i === 0 && pr && pr.w <= 302 && pr.z < 1, `a click on it asks the app to open it large, with where it is and its zoom (${JSON.stringify(pr)})`);
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
  // (1.3.83) Dark picked, the question shown alone: only its parts are turned over, never the page as a whole
  check(await page.evaluate(() => { window.__simplDark = true; window.__simplFocusDark(); const f = getComputedStyle(document.documentElement).filter, w = document.documentElement.hasAttribute('data-simpl-whole-dark'); window.__simplDark = false; window.__simplFocusDark(); return f === 'none' && !w; }),
    'in Dark, the question shown alone has only its parts turned over, never the page whole');
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
  // (1.3.51) Alta's pop-ups as the Mac's own: passed to the app — (1.3.83) once settled, its body as HTML and every one of its
  // buttons — hidden in the page on the app's word, its buttons pressed from the app
  const dlg = await waitFor((p) => p.kind === 'dialog' && p.open === true, 'the pop-up passed to the app');
  check(/Having trouble/.test(dlg.html) && dlg.buttons.map((b) => b.label).join() === 'VIEW INSTRUCTION' && (await page.evaluate(() => document.getElementById('dlg').hasAttribute('data-simpl-dialog'))),
    `an open pop-up of Alta's is passed to the app, its words and buttons (${JSON.stringify(dlg.buttons)})`);
  check(await vis('#vi'), 'it stays in sight in the page until the app says it shows it');
  await page.evaluate(() => window.__simplDialog.hide());
  check((await page.evaluate(() => getComputedStyle(document.querySelector('#dlg [role=dialog]')).visibility)) === 'hidden', 'and is hidden in the page while the app shows it');
  // (1.3.83) hidden, it still reads as it is: never passed again (before, its words read as nothing once hidden)
  const nh = (await posts()).length;
  await page.waitForTimeout(1500);
  const again = (await posts()).slice(nh).filter((p) => p.kind === 'dialog');
  check(again.length === 0, `hidden in the page, it is not passed again, emptied (${again.length} more)`);
  await page.evaluate(() => { document.getElementById('vi').onclick = () => document.getElementById('dlg').remove(); });
  const np = (await posts()).length;
  check(await page.evaluate(() => window.__simplDialog.press(0)), 'the app’s button presses Alta’s');
  await waitFor((p, k) => k >= np && p.kind === 'dialog' && p.open === false, 'the pop-up gone');
  check(!(await page.$('#dlg')) && !(await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-native-dialog'))), 'and when it closes, the app is told and the page left as it was');
  // (1.3.83) Alta's "Knewton Alta" pop-up as Material draws it: fading in, a spinner until its body comes 1.2 s later — words
  // in bold and italic, a list with a link, a formula as MathJax 2 sets one and one as KaTeX does, a picture that takes a
  // moment, a drawing from the page's sprite sheet — an ×, Back (greyed) and Next. Passed once it has settled, whole.
  const nr0 = (await posts()).length;
  const t0 = Date.now();
  await page.evaluate(() => window.mockRichDialog(1200));
  const rd = await waitFor((p, k) => k >= nr0 && p.kind === 'dialog' && p.open, 'the rich pop-up', 80);
  const waited = Date.now() - t0;
  check(waited >= 1500 && (await posts()).slice(nr0).filter((p) => p.kind === 'dialog').length === 1, `passed once, only when its body is in and its picture loaded (${waited} ms after it opened, its spinner first)`);
  check(rd.title === 'Knewton Alta' && !/Knewton Alta/.test(rd.html), `its title from what names it, not again in its body (“${rd.title}”)`);
  check(/<i>you<\/i>/.test(rd.html) && /<b>mastered<\/b>/.test(rd.html) && /<ul><li>Answer the question on screen\.<\/li><li>Stuck\? <a href="simpl-link:0">More Instruction<\/a> explains it\.<\/li><\/ul>/.test(rd.html),
    'its words as they read: italic, bold, a list with its link');
  check(rd.html.includes('\\(\\frac{d}{dx}x^2 = 2x\\)') && rd.html.includes("\\(f'(x) = 2x\\)") && rd.maths === 2 && !/MathJax|d\/dx x/.test(rd.html),
    `its formulas as their TeX — MathJax's own script, KaTeX's annotation — never their drawn copies (${rd.maths})`);
  check(/<img src="http:\/\/localhost:\d+\/mock-alta\/tangent\.svg" width="220" height="120" alt="A tangent line touching a curve">/.test(rd.html) && /<svg[^>]*class="simpl-drawing"><defs><symbol id="alta-bulb"/.test(rd.html) && rd.pics === 2,
    'its picture by its address, its drawing as itself with the symbol it draws from');
  check(JSON.stringify(rd.buttons) === JSON.stringify([{ label: 'Close', primary: false, enabled: true, close: true }, { label: 'Back', primary: false, enabled: false, close: false }, { label: 'Next', primary: true, enabled: true, close: false }]) && rd.ready === 'settled',
    `every one of its buttons: its × as Close, Back greyed out, Next Alta's main one (${JSON.stringify(rd.buttons)})`);
  check((await page.evaluate(() => window.__simplDialog.link(0))) === '' && (await page.evaluate(() => location.hash)) === '#more-instruction', 'a link in it is followed in Alta’s page');
  await page.evaluate(() => window.__simplDialog.hide());
  // (a next step: passed again as it comes, the same pop-up)
  const ns = (await posts()).length;
  check(await page.evaluate(() => window.__simplDialog.press(2)), 'the app’s Next presses Alta’s');
  const s2 = await waitFor((p, k) => k >= ns && p.kind === 'dialog' && p.open, 'the next step', 40);
  check(/<h3>You are all set<\/h3>/.test(s2.html) && s2.buttons.map((b) => b.label + (b.enabled ? '' : '·off')).join() === 'Close,Back,Got it' && s2.buttons[2].primary,
    `its next step is passed as it comes, its words and buttons (${s2.buttons.map((b) => b.label).join(', ')})`);
  const ng = (await posts()).length;
  await page.evaluate(() => window.__simplDialog.press(2));
  await waitFor((p, k) => k >= ng && p.kind === 'dialog' && p.open === false, 'the rich pop-up closed');
  check(!(await page.$('#rdlg')), 'Got it closes it');
  // (1.3.83) a pop-up the app cannot draw as its own is given back to Alta's page — shown as it is, in sight, the focus
  // lifted — and the app told why; once it closes, the question alone again
  for (const [open, id, said, why] of [['mockVideoDialog', 'vdlg', 'Watch this first', /a video/], ['mockFrameDialog', 'fdlg', 'Chat with us', /a frame of another site/], ['mockBareDialog', 'bdlg', 'Are you still there?', /nothing in it could be read/]]) {
    const nv = (await posts()).length;
    await page.evaluate((f) => window[f](), open);
    const vp = await waitFor((p, k) => k >= nv && p.kind === 'dialog' && p.open, `the pop-up (${id})`, 50);
    await page.waitForTimeout(300);
    const asIs = await page.evaluate(() => ({ q: document.documentElement.hasAttribute('data-simpl-q'), shown: document.documentElement.hasAttribute('data-simpl-shown'), hidden: document.documentElement.hasAttribute('data-simpl-native-dialog') }));
    check(vp.inPage === true && why.test(vp.why) && vp.title === said && !asIs.q && asIs.shown && !asIs.hidden && (await vis('#' + id)) && (await vis('header')),
      `a pop-up the app cannot show whole is given back to Alta’s page, shown as it is (“${vp.title}”: ${vp.why})`);
    const nc = (await posts()).length;
    await page.evaluate((i) => document.querySelector('#' + i + ' .shut').click(), id);
    await waitFor((p, k) => k >= nc && p.kind === 'dialog' && p.open === false, `the pop-up (${id}) closed`);
    await page.waitForTimeout(400);
    check((await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-q'))) && !(await vis('header')) && (await vis('#asks')), 'once it closes, the question is shown alone again');
  }
  // (and the student's Show in Alta's Page: the same)
  const na = (await posts()).length;
  await page.evaluate(() => window.mockDialog());
  await waitFor((p, k) => k >= na && p.kind === 'dialog' && p.open && !p.inPage && /Having trouble/.test(p.html || ''), 'the pop-up again');
  const nb2 = (await posts()).length;
  await page.evaluate(() => { window.__simplDialog.hide(); window.__simplDialog.giveBack(); });
  const gb = await waitFor((p, k) => k >= nb2 && p.kind === 'dialog' && p.inPage, 'the pop-up given back');
  check(gb.why === 'the student asked for it' && (await vis('#vi')) && !(await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-q'))), 'Show in Alta’s Page gives it back to the page, in sight');
  await page.evaluate(() => document.getElementById('dlg').remove());
  await page.waitForTimeout(700);
  // (the page's sprite sheet — an svg of symbols its pictures, a pop-up's among them, draw from — is never hidden)
  await page.evaluate(() => { const d = document.createElement('div'); d.innerHTML = '<svg id="sprite" style="position:absolute;width:0;height:0"><defs><symbol id="intro"><circle r="5"/></symbol></defs></svg>'; document.body.appendChild(d); });
  await page.waitForTimeout(500);
  check(await page.evaluate(() => getComputedStyle(document.getElementById('sprite')).display !== 'none' && getComputedStyle(document.getElementById('sprite').parentElement).display !== 'none'), 'the page’s sprite sheet is kept, so its pictures draw');
  await page.evaluate(() => document.getElementById('sprite').parentElement.remove());
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
  // (1.3.83) the empty pop-up: Alta's pop-up coming while its page is out of sight (loading) — 1.3.51–1.3.82 pictured it
  // see-through, the app's sheet empty — is passed whole all the same, its words read from the page as it is
  const nw = (await posts()).length;
  await page.evaluate(() => { const d = document.createElement('div'); d.id = 'wdlg'; d.setAttribute('role', 'dialog'); d.setAttribute('aria-labelledby', 'wt'); d.style.cssText = 'position:fixed;top:20%;left:30%;width:420px;background:#fff;padding:20px'; d.innerHTML = '<h2 id="wt">Knewton Alta</h2><p>Your work is saved as you go.</p><button id="wgot">Got it</button>'; document.body.appendChild(d); document.getElementById('wgot').onclick = () => d.remove(); });
  const wd = await waitFor((p, k) => k >= nw && p.kind === 'dialog' && p.open, 'the pop-up over the loading page', 40);
  const veiled = await page.evaluate(() => getComputedStyle(document.body).opacity);
  check(!wd.inPage && wd.title === 'Knewton Alta' && /<p>Your work is saved as you go\.<\/p>/.test(wd.html) && wd.buttons.map((b) => b.label).join() === 'Got it' && veiled === '0',
    `a pop-up over Alta’s page while it is out of sight is passed whole — its title, its words, its Got it (page opacity ${veiled}: ${wd.html})`);
  await page.evaluate(() => window.__simplDialog.press(0));
  await waitFor((p, k) => k >= nw && p.kind === 'dialog' && p.open === false, 'the pop-up over the loading page closed');
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
  // (1.3.83) Dark picked: Alta's page as it is (here its overview) drawn dark — its colours turned round, its pictures kept
  // — unless it has a dark look of its own; and light again on Light
  await waitFor((p) => p.kind === 'shown', 'the overview in sight');
  await page.evaluate(() => { document.body.style.background = '#16181c'; window.__simplDark = true; window.__simplFocusDark(); });
  const own = await page.evaluate(() => ({ whole: document.documentElement.hasAttribute('data-simpl-whole-dark'), f: getComputedStyle(document.documentElement).filter }));
  await page.evaluate(() => { document.body.style.background = ''; window.__simplFocusDark(); document.body.insertAdjacentHTML('beforeend', '<img id="dkimg" width="8" height="8" src="data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACwAAAAAAQABAAACAkQBADs=">'); });
  const wdk = await page.evaluate(() => ({ whole: document.documentElement.hasAttribute('data-simpl-whole-dark'), f: getComputedStyle(document.documentElement).filter, img: getComputedStyle(document.getElementById('dkimg')).filter }));
  await page.evaluate(() => { window.__simplDark = false; window.__simplFocusDark(); document.getElementById('dkimg').remove(); });
  const lt = await page.evaluate(() => getComputedStyle(document.documentElement).filter);
  check(!own.whole && own.f === 'none' && wdk.whole && /invert\(0\.92\)/.test(wdk.f) && /invert\(1\)/.test(wdk.img) && lt === 'none',
    `in Dark, Alta's page as it is is drawn dark, its pictures kept (${wdk.f} · ${wdk.img}), not a page dark of its own, and light again on Light`);

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
} finally {
  await browser.close();
  stop();
}

console.log(failed ? `\n${failed} failed` : '\nAll checks passed.');
process.exit(failed ? 1 : 0);

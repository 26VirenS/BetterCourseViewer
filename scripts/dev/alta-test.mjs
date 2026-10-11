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
const PORT = 8881;
const mock = spawn(process.execPath, [join(ROOT, 'scripts/dev/mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
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
  const waitFor = async (test, what) => {
    for (let i = 0; i < 60; i++) { const p = (await posts()).filter(test); if (p.length) return p[p.length - 1]; await page.waitForTimeout(100); }
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
    && cv.details[1].value === 'Not started' && cv.details[2].label === 'Grade',
    `the cover read: its name and details (${cv && cv.details.map((d) => d.label + ': ' + d.value).join(' · ')})`);
  check(!!cv && cv.note === 'Complete by 11:59pm today to earn 100% additional credit.' && cv.start === 'Start' && cv.review === true,
    `its note, its Start as Alta words it, and Review Instruction (${cv && cv.start})`);
  check(!!cv && cv.topics.length === 2 && cv.topics[0].name === 'The Limit of a Function' && cv.topics[0].estimate === '6\u201312 questions'
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
  const bad = await waitFor((p, k) => k >= nb && p.kind === 'focus' && p.state === 'unknown', 'the page not recognised');
  check((await vis('header')) && (await vis('h1')) && !(await page.evaluate(() => document.documentElement.hasAttribute('data-simpl-q'))) && (await page.evaluate(() => getComputedStyle(document.body).backgroundColor)) === 'rgb(238, 241, 244)',
    'then Alta’s page is given back as it is, its own ground too');
  check(/no part/.test(bad.reason) && bad.shape.buttons.includes('back to top') && Array.isArray(bad.shape.flow) && !JSON.stringify(bad.shape).includes('derivative of f'),
    `and the app is told why, with what the page held — its makeup, its buttons — never the question (“${bad.reason}”)`);
  const nr = (await posts()).length;
  await page.evaluate(() => { window.__flow.appendChild(window.__q); });
  await waitFor((p, k) => k >= nr && p.kind === 'focus' && p.state === 'question', 'the question again');
  check(!(await vis('header')) && (await vis('#asks')), 'when the page is a question again, it is shown alone again');

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
} finally {
  await browser.close();
  stop();
}

console.log(failed ? `\n${failed} failed` : '\nAll checks passed.');
process.exit(failed ? 1 : 0);

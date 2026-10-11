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

  // Alta opens on the assignment's overview: its objectives read from whatever Alta's page reads
  await page.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a1`);
  const ov = await waitFor((p) => p.kind === 'overview' && p.objectives?.length && p.objectives[0].id === 'lo1', 'overview');
  check(ov.name === 'Differentiation Rules 2' && ov.objectives.length === 3 && ov.objectives[0].name === 'Combine the product and quotient rules' && ov.objectives[0].low === 4 && ov.objectives[0].high === 9 && ov.started === false && ov.percent === 0 && !!ov.due,
    `the overview: the assignment, its objectives with their estimates, not started, when it is due (${ov.objectives.map((o) => o.name).join(' · ')})`);
  check(await page.evaluate(() => window.__simplAlta === undefined && !document.getElementById('simpl-theme')), 'it gives the app no hands in the page and no style of its own');

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

// Simpl for Mac 1.3.17: the Knewton Alta popup's page script (mac/Simpl/Engine/AltaSession.swift, AltaHook.source).
// First in a stand-in page: it does nothing on any site but Alta's. Then in Chromium on the mock's Alta player
// (scripts/dev/mock-canvas.mjs, /mock-alta/…): it passes the app each question and the objectives' mastery from Alta's
// content answer, never the answer key or who the student is; it puts an answer given in the popup into Alta's own
// question, presses Alta's Check and reads the verdict back, and presses Continue for the next question.
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
  check(posted.length === 0 && win.fetch === realFetch && win.XMLHttpRequest.prototype.open === open0 && !win.__simplAlta, 'on a page that is not a web page (about:blank) it does nothing (it is put only in the popup’s own view, which holds Alta’s launch)');
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
  // (1.3.19) an overview that names its objectives only in its words, with START a link in a web component
  await page.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a2`);
  await page.waitForTimeout(300);
  const p2 = await page.evaluate(() => window.__simplAlta.peek());
  check(p2.start && p2.word === 'START' && p2.overview.name === 'Differentiation Rules 2' && p2.overview.statusText === 'Not started' && p2.overview.dueText === 'Monday, Oct 12 11:59pm PDT',
    `on an overview drawn in words: Start found inside a web component, and the title, due date and status read (${p2.overview.dueText} · ${p2.overview.statusText})`);
  check(p2.overview.objectives.length === 2 && p2.overview.objectives[0].name === 'Combine the product and quotient rules' && p2.overview.objectives[0].low === 4 && p2.overview.objectives[0].high === 9 && p2.overview.objectives[1].high === 6,
    `and its objectives, each under its “Estimated … questions” (${p2.overview.objectives.map((o) => `${o.name} ${o.low}–${o.high}`).join(' · ')})`);
  const d2 = await page.evaluate(() => window.__simplAlta.diag());
  check(d2.start === 1 && d2.estimates === 2 && d2.buttons.some((b) => /^a: start$/.test(b)) && !JSON.stringify(d2).includes('lti-secret'), 'Copy Alta Details says what it found: Start, the estimates, the buttons’ words');

  // Alta opens on the assignment's overview: its objectives read from whatever Alta's page reads, and Start found
  await page.goto(`${BASE}/mock-alta/learn/course/c1/assignment/a1`);
  const posts = async () => (await page.evaluate(() => window.__posted.slice())).map((s) => JSON.parse(s));
  const waitFor = async (test, what) => {
    for (let i = 0; i < 60; i++) { const p = (await posts()).filter(test); if (p.length) return p[p.length - 1]; await page.waitForTimeout(100); }
    throw new Error(`no ${what} came`);
  };
  const content = async (type) => waitFor((p) => p.kind === 'content' && p.question?.type === type, `${type} question`);

  const ov = await waitFor((p) => p.kind === 'overview' && p.objectives?.length, 'overview');
  check(ov.name === 'Differentiation Rules 2' && ov.objectives.length === 3 && ov.objectives[0].id === 'lo1' && ov.objectives[0].name === 'Combine the product and quotient rules' && ov.objectives[0].low === 4 && ov.objectives[0].high === 9 && ov.started === false && ov.percent === 0 && !!ov.due,
    `the overview: the assignment, its objectives with their estimates, not started, when it is due (${ov.objectives.map((o) => o.name).join(' · ')})`);
  const peek = await page.evaluate(() => window.__simplAlta.peek());
  check(peek.start && /start/i.test(peek.word), `Alta’s own Start is found on it (“${peek.word}”)`);
  check((await page.evaluate(() => window.__simplAlta.begin())).ok, 'and pressed');
  await page.waitForURL(/\/practice$/);

  const c1 = await content('mcq');
  const q1 = c1.question;
  check(c1.name === 'Linear, Polynomial, and Piecewise Functions' && c1.percent === 62 && c1.objectives.length === 3 && c1.targets[1].id === 'lo2' && c1.current.id === 'lo2', 'it passes the assignment, its objectives and their mastery');
  check(q1.responseId === 'r-mcq' && q1.options.length === 3 && q1.options[0].label === '$_f(x) = 3x + 2$_' && /Which function is linear/.test(q1.prompt) && !q1.multiple, 'and the question: what it asks and its options, as Alta’s answer has them');
  const put = await page.evaluate(() => window.__simplAlta.answer({ responseId: 'r-mcq', type: 'mcq', picks: [0], labels: ['$_f(x) = 3x + 2$_'], values: ['0'] }));
  check(put.ok && (await page.isChecked('#o0')) && !(await page.isChecked('#o1')), 'an option picked in the popup is picked in Alta’s question, as a click would');
  check((await page.evaluate(() => window.__simplAlta.check())).ok, 'Alta’s Check Answer is pressed');
  const f1 = await waitFor((p) => p.kind === 'feedback', 'verdict');
  check(f1.verdict === 'correct' && /Correct/.test(f1.text) && f1.next, `Alta’s verdict comes back, with its words, and Continue is there: ${f1.verdict} “${f1.text}”`);

  check((await page.evaluate(() => window.__simplAlta.next())).ok, 'Continue presses Alta’s Next Question');
  const c2 = await content('clozetext');
  check(c2.question.template && c2.question.template.split('{{response}}').length === 3 && c2.percent === 70, 'the next question comes as Alta asks for it: two blanks, the mastery moved on');
  const put2 = await page.evaluate(() => window.__simplAlta.answer({ responseId: 'r-cloze', type: 'clozetext', blanks: ['5', '2'] }));
  const typed = await page.$$eval('#q input[type=text]', (xs) => xs.map((x) => x.value));
  check(put2.ok && typed.join() === '5,2', 'the blanks typed in the popup are typed into Alta’s');
  await page.evaluate(() => window.__simplAlta.check());
  const f2 = await waitFor((p) => p.kind === 'feedback' && p.verdict === 'incorrect', 'a second verdict');
  check(f2.verdict === 'incorrect' && /linear function/.test(f2.text), 'a wrong answer comes back wrong, with Alta’s explanation');

  await page.evaluate(() => window.__simplAlta.next());
  const c3 = await content('custom');
  check(c3.question.custom === 'desmos_blank_graph_question' && c3.current.id === 'lo3', 'a Desmos graph question is passed as what it is (the popup shows Alta’s page for it)');

  const all = JSON.stringify(await posts());
  const leaked = SECRETS.filter((s) => all.includes(s));
  check(!leaked.length, `never the answer key, the student’s ids or the school’s launch data (${leaked.join(', ') || 'none'})`);
} finally {
  await browser.close();
  stop();
}

console.log(failed ? `\n${failed} failed` : '\nAll checks passed.');
process.exit(failed ? 1 : 0);

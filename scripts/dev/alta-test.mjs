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
  await page.waitForTimeout(1600);
  check(!(await page.$('#welcome')), 'Alta’s welcome pop-up is put away by its own Got it');

  const c1 = await content('mcq');
  const q1 = c1.question;
  check(c1.name === 'Linear, Polynomial, and Piecewise Functions' && c1.percent === 62 && c1.objectives.length === 3 && c1.targets[1].id === 'lo2' && c1.current.id === 'lo2', 'it passes the assignment, its objectives and their mastery');
  check(q1.responseId === 'r-mcq' && q1.options.length === 3 && q1.options[0].label === '$_f(x) = 3x + 2$_' && /Which function is linear/.test(q1.prompt) && !q1.multiple, 'and the question: what it asks and its options, as Alta’s answer has them');
  const put = await page.evaluate(() => window.__simplAlta.answer({ responseId: 'r-mcq', type: 'mcq', picks: [0], labels: ['$_f(x) = 3x + 2$_'], values: ['0'] }));
  check(put.ok && (await page.isChecked('#o0')) && !(await page.isChecked('#o1')), 'an option picked in the popup is picked in Alta’s question, as a click would');
  check((await page.evaluate(() => window.__simplAlta.check())).ok, 'Alta’s Check Answer is pressed');
  const f1 = await waitFor((p) => p.kind === 'feedback', 'verdict');
  check(f1.verdict === 'correct' && /Correct/.test(f1.text) && f1.next, `Alta’s verdict comes back, with its words, and Continue is there: ${f1.verdict} “${f1.text}”`);
  // (1.3.21) the mastery moves when Alta's answer to the check says so — its question is not asked for again
  const moved = await waitFor((p) => p.kind === 'overview' && p.percent === 66, 'progress after the check');
  check(moved.targets.length === 3 && moved.targets[1].id === 'lo2' && moved.targets[1].progress === 0.59 && moved.progress === 0.66,
    `after a check, the mastery Alta’s answer to it carries is passed: ${moved.percent}%, the objective on screen at ${moved.targets[1].progress}`);
  const bar = await waitFor((p) => p.kind === 'meter' && p.percent === 66, 'Alta’s mastery bar');
  check(bar.bars.some((b) => b.label === 'mastery' && Math.abs(b.value - 0.66) < 1e-9), `and Alta’s own mastery bar is read as it moves (${bar.percent}%)`);

  check((await page.evaluate(() => window.__simplAlta.next())).ok, 'Continue presses Alta’s Next Question');
  const c2 = await content('clozetext');
  check(c2.question.template && c2.question.template.split('{{response}}').length === 3 && c2.percent === 74, 'the next question comes as Alta asks for it: two blanks, the mastery moved on');
  const put2 = await page.evaluate(() => window.__simplAlta.answer({ responseId: 'r-cloze', type: 'clozetext', blanks: ['5', '2'] }));
  const typed = await page.$$eval('#q input[type=text]', (xs) => xs.map((x) => x.value));
  check(put2.ok && typed.join() === '5,2', 'the blanks typed in the popup are typed into Alta’s');
  await page.evaluate(() => window.__simplAlta.check());
  const f2 = await waitFor((p) => p.kind === 'feedback' && p.verdict === 'incorrect', 'a second verdict');
  check(f2.verdict === 'incorrect' && /linear function/.test(f2.text), 'a wrong answer comes back wrong, with Alta’s explanation');

  // (1.3.20) a maths answer typed into Learnosity's formula field (MathQuill's own text box underneath)
  await page.evaluate(() => window.__simplAlta.next());
  const cf = await content('clozeformula');
  check(cf.question.template === "k'(1) = {{response}}", 'a maths question comes with its template, k′(1) = ▢');
  const putF = await page.evaluate(() => window.__simplAlta.answer({ responseId: 'r-formula', type: 'clozeformula', blanks: ['-14/9'] }));
  const typedF = await page.$eval('#q .mq-editable-field textarea', (x) => x.value);
  check(putF.ok && typedF === '-14/9', `the answer typed in the popup is typed into Alta’s maths field (${typedF})`);
  // (the mock's own question words are left untypeset; Alta's are typeset — set them plainly for this)
  await page.evaluate(() => { document.querySelector('.alta-stimulus').textContent = 'Find the derivative at 1.'; });
  await page.evaluate(() => window.__simplAlta.focus({ on: true, bar: true }));
  await page.waitForTimeout(700);
  const fs = (await posts()).filter((p) => p.kind === 'focus').pop();
  check(fs && fs.found && fs.raw === false, 'a maths field’s own hidden copy of what is typed ($\\frac{1}{2}$) is not taken for raw maths');
  await page.evaluate(() => window.__simplAlta.focus({ on: false }));
  await page.evaluate(() => window.__simplAlta.check());
  const fF = await waitFor((p) => p.kind === 'feedback' && /Correct/.test(p.text) && p !== f1, 'a maths verdict');
  check(fF.verdict === 'correct', 'and Alta marks it right');

  // (1.3.24) shown alone, the page stays where the student scrolled it while they type, and while the page changes
  await page.setViewportSize({ width: 900, height: 260 });
  await page.evaluate(() => window.__simplAlta.focus({ on: true, bar: true, css: 'body { padding-bottom: 600px !important; }' }));
  await page.waitForTimeout(300);
  await page.evaluate(() => window.scrollTo(0, 140));
  const y0 = await page.evaluate(() => window.scrollY);
  for (let i = 0; i < 6; i++) { await page.evaluate((i) => { document.querySelector('.mq-editable-field').classList.toggle('mq-blink'); document.querySelector('.mq-root-block').textContent = 'x'.repeat(i); }, i); await page.waitForTimeout(250); }
  const y1 = await page.evaluate(() => window.scrollY);
  await page.evaluate(() => { const d = document.createElement('div'); d.textContent = 'elsewhere'; document.body.appendChild(d); });
  await page.waitForTimeout(800);
  const y2 = await page.evaluate(() => window.scrollY);
  check(y0 === 140 && y1 === 140 && y2 === 140, `typing in the question, and changes elsewhere, leave the page where it was scrolled (${y0} → ${y1} → ${y2})`);
  check((await page.$$eval('.lrn-formula-keyboard-key', (ks) => ks.length)) === 8, 'Alta’s maths keypad is kept with its question');
  await page.evaluate(() => window.__simplAlta.focus({ on: false }));
  await page.setViewportSize({ width: 1280, height: 720 });

  await page.evaluate(() => window.__simplAlta.next());
  const c3 = await content('custom');
  check(c3.question.custom === 'desmos_blank_graph_question' && c3.current.id === 'lo3', 'a Desmos graph question is passed as what it is (the popup shows Alta’s own question for it)');

  // (1.3.21) any kind of question: Alta's own, shown alone — the rest of its page hidden, then shown again as it was
  await page.waitForSelector('#graph');
  const styleBefore = await page.$eval('header', (h) => h.getAttribute('style'));
  const fo = await page.evaluate(() => window.__simplAlta.focus({ on: true, hint: 'in the graphing window.' }));
  const vis = async (sel) => page.$eval(sel, (e) => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; }).catch(() => false);
  check(fo.found && !(await vis('header')) && !(await vis('#help')) && !(await vis('.obj')) && !(await vis('main > h2')),
    'a question of a kind the popup does not draw is shown alone: Alta’s header, help button and side panels out of sight');
  check((await vis('#graph')) && (await vis('#check')) && (await vis('.alta-stimulus')) && (await page.$eval('#check', (b) => b.disabled)),
    'with the question, what it asks (outside Learnosity’s box) and Alta’s Check — greyed out until it is answered — still there');
  await page.click('#graph');
  const said = (await posts()).length;
  await page.click('#check');
  const fp = await waitFor((p, k) => k >= said && p.kind === 'feedback' && /Incorrect/.test(p.text) && p.verdict === 'incorrect', 'a verdict from Alta’s own Check');
  check(!!fp && (await vis('#fb')), 'Alta’s own Check pressed there is heard, and its verdict stays in sight');
  await page.evaluate(() => window.__simplAlta.focus({ on: false }));
  check((await vis('header')) && (await vis('#help')) && ((await page.$eval('header', (h) => h.getAttribute('style'))) || '') === (styleBefore || '') && (await page.$eval('#mbar', (e) => e.style.width)) === '120px', 'Alta’s Page puts every part back as it was');

  // (1.3.23) every question in Alta's own answer box, in the popup's colours, its Check pressed from the popup's bar
  await page.evaluate(() => window.__simplAlta.next());
  await page.waitForFunction(() => { const c = document.getElementById('check'); return c && !c.hidden && c.disabled; });
  const n0 = (await posts()).length;
  await page.evaluate(() => window.__simplAlta.focus({ on: true, hint: 'in the graphing window.', bar: true, css: 'body { color: rgb(9, 9, 9) !important; }' }));
  const left = async (sel) => page.$eval(sel, (b) => b.getBoundingClientRect().left);
  check((await left('#check')) < -5000 && (await page.$eval('body', (b) => getComputedStyle(b).color)) === 'rgb(9, 9, 9)' && (await vis('#graph')) && (await vis('.alta-stimulus')) && !(await vis('header')),
    'Alta’s answer box shown alone in the popup’s colours, its own Check moved aside for the popup’s bar');
  check(!(await vis('#report')) && (await left('#instr')) < -5000, 'Alta’s Feedback put away and its More Instruction moved aside for the popup’s bar');
  const s0 = await waitFor((p, k) => k >= n0 && p.kind === 'focus', 'the question found');
  check(s0.instruct === true && s0.lessonToo === false, 'the popup is told Alta offers More Instruction (and that no lesson sits above the question)');
  check((await page.evaluate(() => window.__simplAlta.instruct())).ok, 'the popup’s More Instruction presses Alta’s');
  const d1 = await waitFor((p, k) => k >= n0 && p.kind === 'focus' && p.dialog === true, 'Alta’s pop-up');
  check(!!d1 && (await vis('#trouble')) && (await vis('#goback')), 'Alta’s Having trouble? pop-up is shown, and the popup is told (its bar put away under it)');
  const n2 = (await posts()).length;
  await page.click('#goback');
  const d0 = await waitFor((p, k) => k >= n2 && p.kind === 'focus' && p.dialog === false, 'the pop-up closed');
  check(!!d0 && !(await page.$('#trouble')), 'Go Back closes it, and the popup is told');

  // (1.3.28) View Instruction: Alta's lesson, shown alone like a question, with Continue for the popup's bar
  await page.evaluate(() => window.__simplAlta.instruct());
  await page.waitForSelector('#view');
  const n3 = (await posts()).length;
  await page.click('#view');
  // (1.3.34) the lesson and its question come in one answer, the lesson first: the question is the one passed
  const both = await waitFor((p, k) => k >= n3 && p.kind === 'content' && p.withLesson === true, 'the lesson with its question');
  check(both.question && both.question.type === 'custom' && both.question.purpose === 'ASSESSES', `when Alta sends a lesson and its question together, the question is the one the popup takes (${both.question && both.question.type})`);
  const rawSeen = await waitFor((p, k) => k >= n3 && p.kind === 'focus' && p.raw === true, 'raw maths');
  check(!!rawSeen, 'the lesson’s maths still in its raw words is told (the popup covers it meanwhile)');
  const greyed = await waitFor((p, k) => k >= n3 && p.kind === 'focus' && p.nextAny === true && p.next === false, 'a greyed-out Continue');
  check(!!greyed, 'a lesson’s Continue greyed out until it is read is told (the bar shows Continue, greyed out)');
  const ls = await waitFor((p, k) => k >= n3 && p.kind === 'focus' && p.found && p.hasCheck === false && p.next === true && p.raw === false, 'the lesson');
  check(!!ls, 'and told again once it is typeset and its Continue is ready');
  check((await page.$('.tex .mjx-chtml')) && !(await page.$('.garbled')) && (await page.$eval('#MathJax_Font_Test', (e) => e.offsetWidth > 0)),
    'MathJax’s own hidden helpers are left alone, so it measures its fonts and sets the maths out right');
  check(!!ls && (await vis('.alta-lesson')) && !(await vis('header')) && !(await vis('.obj')) && !(await vis('#lfeedback')) && (await left('#lcont')) < -5000,
    'Alta’s lesson is shown alone (its header, title and objective card hidden, its Feedback put away), its Continue moved aside for the popup’s bar');
  const n4 = (await posts()).length;
  check((await page.evaluate(() => window.__simplAlta.next())).ok, 'the popup’s Continue presses the lesson’s');
  const back = await waitFor((p, k) => k >= n4 && p.kind === 'focus' && p.found && p.hasCheck === true, 'the question again');
  check(!!back && (await vis('#graph')) && (await vis('.alta-stimulus')) && !(await vis('.alta-lesson')), 'and the question comes under the lesson, shown alone with what it asks — the lesson above it hidden');
  check(back.lessonToo === true, 'the popup is told the lesson is still there (its bar offers View Instruction)');
  // (1.3.31) View Instruction: the lesson again in the question's place, and back
  await page.evaluate(() => window.__simplAlta.focus({ on: true, bar: true, view: 'lesson' }));
  await page.waitForTimeout(300);
  check((await vis('.alta-lesson')) && !(await vis('#graph')), 'View Instruction shows the lesson again, the question hidden');
  await page.evaluate(() => window.__simplAlta.focus({ on: true, bar: true, view: 'question' }));
  await page.waitForTimeout(300);
  check((await vis('#graph')) && !(await vis('.alta-lesson')), 'Back to Question shows the question again, the lesson hidden');
  // (a button the bar stands for, come inside what is kept — a change the trim otherwise leaves alone — is moved aside)
  await page.evaluate(() => { const b = document.createElement('button'); b.id = 'late'; b.textContent = 'Continue'; document.querySelector('.learnosity-response').appendChild(b); });
  await page.waitForTimeout(1200);
  check((await left('#late')) < -5000, 'a Continue that comes inside the question later is moved aside for the bar too');
  await page.evaluate(() => document.getElementById('late').remove());
  check(s0.found && s0.check === false, 'the popup is told the question is found and its Check is not ready (nothing answered yet)');
  await page.click('#graph');
  const s1 = await waitFor((p, k) => k >= n0 && p.kind === 'focus' && p.check === true, 'Check ready');
  check(s1.found, 'answered in Alta’s box, the popup is told its Check can be pressed');
  const n1 = (await posts()).length;
  check((await page.evaluate(() => window.__simplAlta.check())).ok, 'the popup’s Check presses Alta’s own, moved aside');
  const v1 = await waitFor((p, k) => k >= n1 && p.kind === 'feedback' && p.verdict === 'incorrect', 'a verdict from the popup’s Check');
  check(/Incorrect/.test(v1.text) && v1.next && (await left('#next')) < -5000, 'its verdict comes back, with Alta’s Next moved aside for the popup’s Continue');
  await page.evaluate(() => window.__simplAlta.focus({ on: false }));
  check(!(await page.$('#simpl-theme')) && (await left('#next')) >= 0 && (await vis('header')), 'Alta’s Page takes the popup’s colours out and puts Next back');

  const all = JSON.stringify(await posts());
  const leaked = SECRETS.filter((s) => all.includes(s));
  check(!leaked.length, `never the answer key, the student’s ids or the school’s launch data (${leaked.join(', ') || 'none'})`);
} finally {
  await browser.close();
  stop();
}

console.log(failed ? `\n${failed} failed` : '\nAll checks passed.');
process.exit(failed ? 1 : 0);

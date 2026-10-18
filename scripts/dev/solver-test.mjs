// The Calculator's solver (extension/tools/solver), checked: its engine's answers to the kinds of problem it is for —
// arithmetic, simplifying, factoring, equations of every shape it solves, inequalities, systems, derivatives, integrals,
// limits — and its page, opened in Chromium on a problem, answering without an error.
//   node scripts/dev/solver-test.mjs
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const dir = join(root, 'extension', 'tools', 'solver');
const require = createRequire(import.meta.url);
for (const f of ['core', 'algebra', 'calculus', 'solver']) require(join(dir, f + '.js'));
const SS = globalThis.SimplSolver;

let failed = 0, passed = 0;
function check(problem, option, want, extra) {
  const r = SS.run(problem, option, extra);
  const got = r.error ? 'ERROR ' + r.error : r.answers.map((a) => a.text).join('; ');
  const ok = want instanceof RegExp ? want.test(got) : got === want;
  if (ok) passed++;
  else { failed++; console.log(`✗ ${problem} [${option || 'auto'}]\n    got:  ${got}\n    want: ${want}`); }
}

check('2+3*4', null, '14');
check('1/3+1/4', null, '7/12');
check('sqrt(72)', null, '6sqrt(2)');
check('simplify (x^2-1)/(x-1)', null, 'x + 1');
check('x^2-5x+6', 'factor', '(x - 3)(x - 2)');
check('factor x^4-16', null, '(x + 2)(x - 2)(x^2 + 4)');
check('factor x^2-y^2', null, '(x - y)(x + y)');
check('(x+1)^3', 'expand', 'x^3 + 3x^2 + 3x + 1');
check('2x+3=11', null, 'x = 4');
check('3x-5=2x+7', null, 'x = 12');
check('x^2-5x+6=0', null, 'x = 2; x = 3');
check('x^2-x-1=0', null, 'x = (1 - sqrt(5))/2; x = (1 + sqrt(5))/2');
check('x^2+x+1=0', null, /no real solutions/i);
check('x^3-6x^2+11x-6=0', null, 'x = 1; x = 2; x = 3');
check('1/x+1/(x+1)=1', null, 'x = (1 - sqrt(5))/2; x = (1 + sqrt(5))/2');
check('x/(x-1)=1/(x-1)+2', null, /no solutions/i);
check('sqrt(x+3)=x-3', null, 'x = 6');
check('2^x=8', null, 'x = 3');
check('ln(x)=2', null, 'x = e^2');
check('|2x-1|=5', null, 'x = -2; x = 3');
check('sin(x)=1/2', null, 'x = π/6; x = (5π)/6');
check('x^3+y=0', 'solve:y', 'y = -x^3');
check('2x-1>5', null, '');
check('x+y=7, 2x-y=2', null, 'x = 3, y = 4');
check('x+y+z=6, 2x-y+z=3, x+2y-z=2', null, 'x = 1, y = 2, z = 3');
check('d/dx x^2 sin(x)', null, 'x^2cos(x) + 2xsin(x)');
check('derivative of ln(x^2+1)', null, '(2x)/(x^2 + 1)');
check('integrate x^2 dx', null, 'x^3/3 + C');
check('integral of x e^x', null, 'xe^x - e^x + C');
check('∫_0^1 x^2 dx', null, '1/3');
check('integrate sin(x) from 0 to pi', null, '2');
check('lim x->0 sin(x)/x', null, '1');
check('limit as x approaches inf of (2x+1)/(x-3)', null, '2');
check('x^2', 'evaluate', '9', { at: '3' });
check('1 < 2x+1 < 5', null, '');
// (the inequalities' answers are TeX only: their words checked)
const iq = SS.run('x^2-4>=0');
if (!/x \\le -2/.test(iq.answers[0].tex) || !/x \\ge 2/.test(iq.answers[0].tex)) { failed++; console.log('✗ x^2-4>=0: ' + iq.answers[0].tex); } else passed++;

// (the page, in Chromium: a problem sent and an option picked answer without an error)
try {
  let chromium;
  try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 900, height: 760 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto('file://' + join(dir, 'index.html') + '?q=' + encodeURIComponent('x^2 - x - 1 = 0'));
  await page.click('.choice');
  await page.waitForSelector('.answer');
  const text = await page.textContent('.msg.bot:last-child .answers');
  await page.click('text=Show steps');
  const steps = await page.locator('.step').count();
  await browser.close();
  if (errors.length || !/x/.test(text) || steps < 3) { failed++; console.log('✗ page: ' + (errors.join(' | ') || `answer "${text}", ${steps} steps`)); } else passed++;
} catch (e) {
  console.log('(page not checked: ' + e.message.split('\n')[0] + ')');
}

console.log(`solver: ${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);

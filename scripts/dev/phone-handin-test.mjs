// Hand in from your phone (2.99.96): the hand-in panel's QR code of the assignment's own hand-in, and the receipt
// once the phone hands in.
//
// An extension cannot be the phone's server as the apps are (Send from Phone), so the code holds the assignment's
// address alone — ?bcv=submit, which opens straight to Hand In where Simpl is on the phone — and the page asks the
// school while the sheet is open. This checks the code reads as that address (drawn again here from the same library,
// module for module), and that a hand-in made elsewhere — the phone, stood in for by a request to the mock — closes the
// sheet and shows the receipt.
// Run: node scripts/dev/phone-handin-test.mjs
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { afterMigration, launchExtension } from './harness.mjs';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const qrcode = require(join(root, 'extension', 'lib', 'vendor', 'qrcode.js'));
const OUT = join(root, 'scripts/dev/out');
mkdirSync(OUT, { recursive: true });
const PORT = 8817;
const BASE = `http://localhost:${PORT}`;
const extDir = join(tmpdir(), `bcv-ph-ext-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*')) cs.matches.push(`${BASE}/*`); // (the on-demand modules keep their never-matching group: a page asks for them, as it does in a browser)
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };
const userDataDir = join(tmpdir(), `bcv-ph-profile-${Date.now()}`);
const { context, sw } = await launchExtension(chromium, userDataDir, extDir, { viewport: { width: 1400, height: 900 } });
try {
  await afterMigration(sw);
  await sw.evaluate(async () => {
    await self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'setup:flow': 3, 'whatsnew:seen': self.BCV.api.runtime.getManifest().version });
  });
  const page = await context.newPage();
  page.on('pageerror', (e) => failures.push(`page error: ${e.message}`));
  const asg = '/courses/104/assignments/4002';
  await page.goto(`${BASE}${asg}`, { waitUntil: 'commit' });
  await page.waitForSelector('.bcv-asg__pill.is-go', { timeout: 20000 }).catch(async (e) => { await page.screenshot({ path: join(OUT, '36-phone-handin-fail.png') }); console.log((await page.evaluate(() => document.body.innerText)).slice(0, 600)); throw e; });
  await page.click('.bcv-asg__pill.is-go');
  await page.waitForSelector('.bcv-sheet-ov--card .bcv-hand .bcv-sb__tabs', { timeout: 10000 });
  await page.waitForTimeout(450);
  const go = await page.$('.bcv-sb__phonego');
  const tint = await page.evaluate(() => [document.querySelector('.bcv-sb__phonego'), document.querySelector('.bcv-sb__browse')].map((e) => e && getComputedStyle(e).color));
  check(!!go && (await go.textContent()) === 'Hand in from your phone' && !!tint[0] && tint[0] === tint[1], `the hand-in panel offers Hand in from your phone, a link in the course's colour as browse is: ${JSON.stringify(tint)}`);

  await go.click();
  await page.waitForSelector('.bcv-phone-ov .bcv-phone__qr path', { timeout: 10000 });
  await page.waitForTimeout(400);
  const sheet = await page.evaluate(() => {
    const ov = document.querySelector('.bcv-phone-ov');
    const svg = ov.querySelector('.bcv-phone__qr');
    const r = svg.getBoundingClientRect();
    return {
      title: ov.querySelector('.bcv-sheet__title').textContent,
      steps: [...ov.querySelectorAll('.bcv-phone__step')].map((e) => e.textContent),
      wait: ov.querySelector('.bcv-phone__status').textContent,
      d: svg.querySelector('path').getAttribute('d'),
      box: svg.getAttribute('viewBox'),
      w: Math.round(r.width), h: Math.round(r.height),
      fill: [svg.querySelector('rect').getAttribute('fill'), svg.querySelector('path').getAttribute('fill')],
    };
  });
  // the code drawn again from the address it should hold, module for module
  const want = `${BASE}${asg}?bcv=submit`;
  const q = qrcode(0, 'M');
  q.addData(want);
  q.make();
  const n = q.getModuleCount();
  let d = '';
  for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) if (q.isDark(r, c)) d += `M${c + 4} ${r + 4}h1v1h-1z`;
  check(sheet.d === d && sheet.box === `0 0 ${n + 8} ${n + 8}`, `the code is this assignment's hand-in, ${want}, with its quiet margin (${n} modules)`);
  check(sheet.fill.join() === '#fff,#000' && sheet.w === 168 && sheet.h === 168, `drawn dark on white, 168 px square: ${JSON.stringify({ fill: sheet.fill, w: sheet.w, h: sheet.h })}`);
  check(sheet.title === 'Hand in from your phone' && sheet.steps.length === 3 && /Canvas/.test(sheet.steps[1]) && sheet.wait === 'Waiting for your phone…', `the sheet says what to do and that it is waiting: ${JSON.stringify(sheet.steps)}`);
  await page.screenshot({ path: join(OUT, '36-phone-handin.png') });

  // the phone hands in (a text entry, as the school's own page would send it, with its own session's token)
  const res = await fetch(`${BASE}/api/v1/courses/104/assignments/4002/submissions`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-csrf-token': 'mock+csrf/token=' }, body: JSON.stringify({ submission: { submission_type: 'online_text_entry', body: '<p>Sent from the phone.</p>' } }) });
  check(res.ok, `the phone's hand-in reached the school (${res.status})`);
  const landed = await page.waitForSelector('.bcv-sb__done', { timeout: 20000 }).then(() => true).catch(() => false);
  const after = await page.evaluate(() => ({ sheet: !!document.querySelector('.bcv-phone-ov'), done: document.querySelector('.bcv-sb__done .bcv-sb__h2')?.textContent || '' }));
  check(landed && !after.sheet && after.done === 'Submitted', `the hand-in from the phone closes the sheet and shows the receipt: ${JSON.stringify(after)}`);
} catch (e) {
  failures.push(`crash: ${e.message}`);
  console.log(e);
} finally {
  await context.close().catch(() => {});
  server.kill();
}
console.log(failures.length ? `\n${failures.length} failed` : '\nall passed');
process.exit(failures.length ? 1 : 0);

// A group opened cold, with its course list held back.
//
// /api/v1/courses is the slowest call the app makes, and a group needs its course only for a
// colour, a term and a link back — so the shell draws at once from the group's own call and takes
// the course up afterwards. This checks both moments: that the group is on screen before the
// course list lands, and that the header pill, the shell colour and the About row are then filled
// in exactly once, with the members pill left alone and nothing doubled up across a tab.
// Run: node scripts/dev/group-late-test.mjs
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const PORT = 8803;
const BASE = `http://localhost:${PORT}`;
const extDir = join(tmpdir(), `bcv-late-ext-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) cs.matches.push(`${BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };
const userDataDir = join(tmpdir(), `bcv-late-profile-${Date.now()}`);
const context = await chromium.launchPersistentContext(userDataDir, { channel: 'chromium', headless: true, viewport: { width: 1400, height: 900 }, args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`] });
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'setup:flow': 3, 'whatsnew:seen': v }), manifest.version); // the flow marker too: the background's migration clears the flags for an older flow, and may run after this
  const page = await context.newPage();
  page.on('pageerror', (e) => failures.push(`page error: ${e.message}`));
  await page.route(/\/api\/v1\/courses\?/, async (route) => { await new Promise((r) => setTimeout(r, 2000)); await route.continue().catch(() => {}); });

  // group 66729 belongs to a course ("Academic Success"), so it is the one that gains a course late
  await page.goto(`${BASE}/groups/66729`, { waitUntil: 'commit' });
  await page.waitForSelector('.bcv-detail', { timeout: 20000 });
  const read = () => page.evaluate(() => ({
    title: document.querySelector('.bcv-head h1')?.textContent,
    pills: [...document.querySelectorAll(".bcv-pill--term")].filter((p) => p.getBoundingClientRect().height > 0).map((p) => `${p.tagName}:${p.textContent}`),
    ctxPills: document.querySelectorAll('[data-bcv-ctx-pill]').length,
    courseRows: [...document.querySelectorAll('[data-bcv-course-row]')].map((e) => e.textContent),
    railColor: document.querySelector('.bcv-screen--ctx')?.style.getPropertyValue('--bcv-rail-color'),
    members: [...document.querySelectorAll('.bcv-pill--term')].some((p) => /member/.test(p.textContent)),
  }));

  const early = await read();
  console.log('  early:', JSON.stringify(early));
  check(early.title === 'Attestation Fall 2026 1', `the group is drawn before its course list: ${early.title}`);
  check(early.members, `the members pill is there from the start: ${early.pills.join(' | ')}`);
  check(early.courseRows.length === 0, 'no course row yet');

  await page.waitForFunction(() => document.querySelectorAll('[data-bcv-course-row]').length > 0, null, { timeout: 20000 });
  const late = await read();
  console.log('  late :', JSON.stringify(late));
  check(late.ctxPills === 1 && late.pills.some((p) => p.startsWith('A:') && /Academic Success/.test(p)), `the course replaces the first pill, as a link: ${late.pills.join(' | ')}`);
  check(late.members, `the members pill survived the swap: ${late.pills.join(' | ')}`);
  check(late.courseRows.length === 1 && /Academic Success/.test(late.courseRows[0]), `the About card names the course exactly once: ${late.courseRows.join(' | ')}`);
  check(late.railColor && late.railColor !== '#5856d6', `the shell takes the course's colour: ${late.railColor}`);

  // and a tab after all that: the shell is kept, and nothing is doubled up
  await page.$eval('.bcv-rail__item[data-tab="people"]', (el) => el.click());
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 20000 });
  const afterTab = await read();
  check(afterTab.ctxPills === 1 && afterTab.title === early.title && afterTab.railColor === late.railColor, `a tab afterwards keeps the header as it was: ${afterTab.pills.join(' | ')}`);
  await page.$eval('.bcv-rail__item[data-tab="home"]', (el) => el.click());
  await page.waitForSelector('.bcv-detail', { timeout: 20000 });
  const back = await read();
  check(back.courseRows.length === 1 && back.ctxPills === 1, `back on home the course is named once, not twice: ${back.courseRows.join(' | ')}`);
} catch (e) {
  console.error('crashed:', e?.stack || e);
  failures.push('crash: ' + e.message);
} finally {
  await context.close();
  rmSync(userDataDir, { recursive: true, force: true });
  server.kill();
  rmSync(extDir, { recursive: true, force: true });
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);

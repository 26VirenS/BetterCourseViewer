// The favourite courses in the sidebar: always listed.
//
// (2.99.89) The panel the Courses row opened on hover is gone, and the choice with it: the courses are listed on the
// sidebar for everyone — someone who chose "On hover" before included. This checks they are listed with that old choice
// still stored, and that hovering, focusing or arrowing into the Courses row opens no panel.
// Run: node scripts/dev/side-courses-test.mjs
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync, rmSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { afterMigration, launchExtension } from './harness.mjs';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const OUT = join(root, 'scripts/dev/out');
mkdirSync(OUT, { recursive: true });
const PORT = 8804;
const BASE = `http://localhost:${PORT}`;
const extDir = join(tmpdir(), `bcv-qn-ext-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*')) cs.matches.push(`${BASE}/*`); // (the on-demand modules keep their never-matching group: a page asks for them, as it does in a browser)
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };
const userDataDir = join(tmpdir(), `bcv-qn-profile-${Date.now()}`);
const { context, sw } = await launchExtension(chromium, userDataDir, extDir, { viewport: { width: 1400, height: 900 } }); // (every page's animations MOTION_RATE× faster, and a second browser if the first one's background never starts: harness.mjs)
try {
  await afterMigration(sw); // (the background's setup migration first, or it clears the flags written next)
  const setMode = (mode) => sw.evaluate(async (m) => {
    const S = self.BCV.settings;
    await S.update({ appearance: { sideCourses: m } });
    await self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'setup:flow': 3, 'whatsnew:seen': self.BCV.api.runtime.getManifest().version }); // the flow marker too: the background's migration clears the flags for an older flow, and may run after this
  }, mode);

  await setMode('hover'); // (the old choice, still stored: read by nothing now)
  const page = await context.newPage();
  page.on('pageerror', (e) => failures.push(`page error: ${e.message}`));
  await page.goto(`${BASE}/`, { waitUntil: 'commit' });
  await page.waitForSelector('#bcv-side .bcv-fav', { timeout: 20000 });

  const favs = await page.$$eval('#bcv-side .bcv-fav', (els) => els.map((e) => e.textContent.trim()));
  check(favs.length >= 3, `"On hover" chosen before: the courses are listed down the sidebar all the same (${favs.length})`);
  const sideText = await page.$eval('#bcv-side', (el) => el.innerText);
  check(!/\bnull\b|\bundefined\b/.test(sideText), `and the sidebar has nothing left over: ${sideText.replace(/\n/g, ' / ').slice(0, 120)}`);
  const row = '.bcv-nav__item[data-nav="courses"]';
  check(!(await page.$eval(row, (el) => el.hasAttribute('aria-haspopup') || el.hasAttribute('aria-expanded'))), 'the Courses row says it has no panel');

  await page.hover(row);
  await page.waitForTimeout(400);
  check(!(await page.$('.bcv-quicknav')), 'hovering Courses opens no panel');
  await page.screenshot({ path: join(OUT, '35-side-courses-listed.png'), clip: { x: 0, y: 0, width: 300, height: 760 } });

  await page.$eval(row, (el) => el.focus());
  await page.keyboard.press('ArrowRight');
  await page.waitForTimeout(300);
  check(!(await page.$('.bcv-quicknav')), 'nor does focusing it, or the arrow key');

  // a course on the sidebar opens it
  const first = favs[0];
  await page.click('#bcv-side .bcv-fav');
  await page.waitForFunction(() => /\/courses\/\d+/.test(location.pathname), null, { timeout: 20000 });
  check(/\/courses\/\d+/.test(page.url()), `a course on the sidebar opens it (${page.url().replace(BASE, '')} — ${first})`);
  await page.waitForTimeout(300);
  check((await page.$$('#bcv-side .bcv-fav')).length >= favs.length, 'and the courses stay listed');
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

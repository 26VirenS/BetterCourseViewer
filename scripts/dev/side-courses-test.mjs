// The favourite courses in the sidebar, both ways round.
//
// "Always listed" keeps them under the navigation, as they have always been; "On hover" takes them
// off the sidebar and opens them in a panel beside the Courses row instead (Settings → Appearance,
// and the last step of the guided setup). This checks that each mode leaves the other's furniture
// alone, that the panel holds the same courses in the same order, survives the gap between the row
// and itself, navigates, and answers the keyboard.
// Run: node scripts/dev/side-courses-test.mjs
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync, rmSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { afterMigration } from './harness.mjs';
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
const context = await chromium.launchPersistentContext(userDataDir, { channel: 'chromium', headless: true, viewport: { width: 1400, height: 900 }, args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`] });
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  await afterMigration(sw); // (the background's setup migration first, or it clears the flags written next)
  const setMode = (mode) => sw.evaluate(async (m) => {
    const S = self.BCV.settings;
    await S.update({ appearance: { sideCourses: m } });
    await self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'setup:flow': 3, 'whatsnew:seen': self.BCV.api.runtime.getManifest().version }); // the flow marker too: the background's migration clears the flags for an older flow, and may run after this
  }, mode);

  await setMode('always');
  const page = await context.newPage();
  page.on('pageerror', (e) => failures.push(`page error: ${e.message}`));
  await page.goto(`${BASE}/`, { waitUntil: 'commit' });
  await page.waitForSelector('.bcv-fav', { timeout: 20000 });

  // ---- always: unchanged
  const favs = await page.$$eval('#bcv-side .bcv-fav', (els) => els.map((e) => e.textContent.trim()));
  check(favs.length >= 3, `always: the courses are listed down the sidebar (${favs.length})`);
  check(!!(await page.$('#bcv-side .bcv-side__label')), 'always: the Favorite courses heading is there');
  await page.hover('.bcv-nav__item[data-nav="courses"]');
  await page.waitForTimeout(300);
  check(!(await page.$('.bcv-quicknav')), 'always: hovering Courses opens no panel — the list is already on the sidebar');
  await page.screenshot({ path: join(OUT, '35-side-courses-listed.png'), clip: { x: 0, y: 0, width: 300, height: 760 } });

  // ---- hover: the list moves into the panel
  await page.mouse.move(700, 500); // off the Courses row, or the switch finds the pointer already on it
  await setMode('hover');
  await page.waitForTimeout(400); // the sidebar redraws from the settings change, no reload
  const favsAfter = await page.$$eval('#bcv-side .bcv-fav', (els) => els.length);
  check(favsAfter === 0, `hover: the sidebar no longer lists them (${favsAfter})`);
  const sideText = await page.$eval('#bcv-side', (el) => el.innerText);
  check(!/\bnull\b|\bundefined\b/.test(sideText), `hover: and leaves nothing behind where they were: ${sideText.replace(/\n/g, ' / ').slice(0, 120)}`);
  check(!(await page.$('.bcv-quicknav')), 'hover: nothing is open until the pointer arrives');
  const expandedBefore = await page.$eval('.bcv-nav__item[data-nav="courses"]', (el) => el.getAttribute('aria-expanded'));
  check(expandedBefore === 'false', `hover: the Courses row says it has a panel, closed (${expandedBefore})`);

  await page.hover('.bcv-nav__item[data-nav="courses"]');
  await page.waitForSelector('.bcv-quicknav', { timeout: 5000 });
  const panel = await page.evaluate(() => {
    const el = document.querySelector('.bcv-quicknav');
    const row = document.querySelector('.bcv-nav__item[data-nav="courses"]').getBoundingClientRect();
    const r = el.getBoundingClientRect();
    return {
      courses: [...el.querySelectorAll('.bcv-fav')].map((e) => e.textContent.trim()),
      all: el.querySelector('.bcv-quicknav__all')?.textContent.trim(),
      besideRow: r.left >= row.right && Math.abs(r.top - row.top) < 40,
      onScreen: r.right <= window.innerWidth && r.bottom <= window.innerHeight && r.top >= 0,
      expanded: document.querySelector('.bcv-nav__item[data-nav="courses"]').getAttribute('aria-expanded'),
    };
  });
  console.log('  panel:', JSON.stringify(panel));
  check(panel.courses.length === favs.length && panel.courses.join('|') === favs.join('|'), `hover: the same courses in the same order (${panel.courses.length})`);
  check(panel.besideRow && panel.onScreen, 'hover: the panel sits beside the row and stays on screen');
  check(panel.all === 'All courses', `hover: and a way through to all of them: ${panel.all}`);
  check(panel.expanded === 'true', 'hover: the row reports the panel open');
  await page.waitForTimeout(400);
  await page.screenshot({ path: join(OUT, '35b-side-courses-hover.png'), clip: { x: 0, y: 0, width: 560, height: 760 } });

  // it survives the gap between the row and the panel
  await page.hover('.bcv-quicknav .bcv-fav');
  await page.waitForTimeout(320);
  check(!!(await page.$('.bcv-quicknav')), 'hover: moving the pointer into the panel keeps it open');

  // and a course in it navigates
  const first = await page.$eval('.bcv-quicknav .bcv-fav', (el) => el.textContent.trim());
  await page.click('.bcv-quicknav .bcv-fav');
  await page.waitForSelector('.bcv-rail__item', { timeout: 20000 });
  check(/\/courses\/\d+/.test(page.url()), `hover: a course in the panel opens it (${page.url().replace(BASE, '')} — ${first})`);
  await page.waitForFunction(() => !document.querySelector('.bcv-quicknav'), null, { timeout: 3000 }).catch(() => {}); // (it pops out over a moment)
  check(!(await page.$('.bcv-quicknav')), 'hover: and the panel closes behind it');

  // pointer away closes it
  await page.hover('.bcv-nav__item[data-nav="courses"]');
  await page.waitForSelector('.bcv-quicknav', { timeout: 12000 }); // (the hover's own delay, and a starved machine: three suites run side by side)
  await page.hover('.bcv-nav__item[data-nav="dashboard"]');
  await page.waitForFunction(() => !document.querySelector('.bcv-quicknav'), null, { timeout: 2500 }).catch(() => {}); // (the leave's grace, then its spring out)
  check(!(await page.$('.bcv-quicknav')), 'hover: the pointer leaving both closes it');

  // keyboard: the row opens it and Escape closes it
  await page.$eval('.bcv-nav__item[data-nav="courses"]', (el) => el.focus());
  await page.keyboard.press('ArrowRight');
  await page.waitForSelector('.bcv-quicknav', { timeout: 5000 });
  const focused = await page.evaluate(() => document.activeElement?.className || '');
  check(/bcv-fav/.test(focused), `keyboard: the arrow opens the panel and lands on the first course (${focused})`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-quicknav'), null, { timeout: 2500 }).catch(() => {}); // (its spring out)
  const back = await page.evaluate(() => document.activeElement?.dataset?.nav || document.activeElement?.tagName);
  const stillOpen = !!(await page.$('.bcv-quicknav'));
  check(!stillOpen, `keyboard: Escape closes the panel (open after Escape: ${stillOpen})`);
  check(back === 'courses', `keyboard: and the row has focus again (${back})`);
  // and it does not spring straight back while the row still has focus
  await page.waitForTimeout(300);
  check(!(await page.$('.bcv-quicknav')), 'keyboard: it stays shut while the row keeps focus');
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

// A Canvas tab left open across an extension update (2.98.22).
//
// An update cuts the scripts already running in an open tab off from the extension: Chrome makes
// every call to it throw ("Extension context invalidated"), Safari simply never answers. A screen
// that waited on one — Tools reading its pins, a module loading — used to wait for ever, a skeleton
// that never filled, until the page was reloaded by hand. The page's calls now go through the
// lifeline (lib/settings.js): cut off, it loads the page afresh (the new version) where that loses
// nothing, says so where it would (text being typed), and turns the next page asked for into a real
// load. Both kinds of cut are made here inside a live tab — the calls swapped on the extension's own
// objects, behind the lifeline, as the browser would leave them.
// Run: node scripts/dev/stale-test.mjs
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { afterMigration } from './harness.mjs';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const PORT = 8813;
const BASE = `http://localhost:${PORT}`;
const extDir = join(tmpdir(), `bcv-stale-ext-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*')) cs.matches.push(`${BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };
const userDataDir = join(tmpdir(), `bcv-stale-profile-${Date.now()}`);
const context = await chromium.launchPersistentContext(userDataDir, { channel: 'chromium', headless: true, viewport: { width: 1400, height: 900 }, args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`] });
const t0 = Date.now();
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  await afterMigration(sw);
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'welcome:report1': true, 'tools:welcomed': true, 'setup:flow': 3, 'whatsnew:seen': v }), manifest.version);

  /** A fresh tab on the Dashboard, its loads counted. */
  async function fresh(path = '/') {
    const page = await context.newPage();
    page.on('pageerror', (e) => failures.push(`page error: ${e.message}`));
    const loads = { n: 0 };
    page.on('load', () => { loads.n++; });
    await page.goto(`${BASE}${path}`);
    await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 20000 });
    await page.waitForTimeout(800);
    return { page, loads };
  }
  /** Cuts this tab off from the extension, the way an update leaves it: 'hang' (Safari: nothing ever
   *  answers) or 'throw' (Chrome: every call throws). On the extension's own objects, behind the lifeline. */
  const cut = (mode) => sw.evaluate(async ({ base, mode }) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*`, active: true });
    const [r] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', args: [mode], func: (m) => {
      const raw = self.BCV.life?.raw;
      if (!raw) return 'no lifeline';
      const never = () => new Promise(() => {});
      const dead = () => { throw new Error('Extension context invalidated.'); };
      const f = m === 'hang' ? never : dead;
      raw.runtime.sendMessage = f; raw.storage.local.get = f; raw.storage.local.set = f; raw.storage.local.remove = f;
      return 'cut';
    } });
    return r?.result;
  }, { base: BASE, mode });
  const clearReloadMark = (page) => page.evaluate(() => sessionStorage.removeItem('bcv:reloaded')).catch(() => {});

  // ---- Safari's way: calls that never answer ------------------------------------------------------
  console.log('never answered (Safari)');
  {
    const { page, loads } = await fresh();
    check((await cut('hang')) === 'cut', 'the tab is cut off: nothing it asks the extension ever answers');
    const at = Date.now();
    await page.click('#bcv-app .bcv-nav__item[data-nav="tools"]');
    for (let i = 0; i < 60 && loads.n < 2; i++) await page.waitForTimeout(250); // (a few seconds unanswered, a ping unanswered, then the reload)
    const reloaded = loads.n >= 2;
    const took = Date.now() - at;
    await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 20000 }).catch(() => {});
    await page.waitForFunction(() => document.querySelector('#bcv-app h1')?.textContent === 'Tools' && !document.querySelector('#bcv-main .bcv-skel'), null, { timeout: 15000 }).catch(() => {});
    const after = await page.evaluate(() => ({ h1: document.querySelector('#bcv-app h1')?.textContent, skel: !!document.querySelector('#bcv-main .bcv-skel'), cards: document.querySelectorAll('.bcv-tool-card').length }));
    check(reloaded && took < 12000 && after.h1 === 'Tools' && !after.skel && after.cards > 3, `Tools, which reads its pins from the extension, no longer waits for ever: the page finds itself cut off and loads afresh on its own (${took} ms), and Tools is drawn by the new scripts: ${JSON.stringify(after)}`);
    await page.screenshot({ path: join(root, 'scripts', 'dev', 'out', '90-stale-tools.png') });
    await page.close();
  }

  // ---- Chrome's way: calls that throw -------------------------------------------------------------
  console.log('refused at once (Chrome)');
  {
    const { page, loads } = await fresh();
    await clearReloadMark(page);
    check((await cut('throw')) === 'cut', 'the tab is cut off: every call to the extension throws "Extension context invalidated"');
    const at = Date.now();
    await page.click('#bcv-app .bcv-nav__item[data-nav="tools"]');
    for (let i = 0; i < 40 && loads.n < 2; i++) await page.waitForTimeout(250);
    const took = Date.now() - at;
    await page.waitForFunction(() => document.querySelector('#bcv-app h1')?.textContent === 'Tools' && !document.querySelector('#bcv-main .bcv-skel'), null, { timeout: 15000 }).catch(() => {});
    check(loads.n === 2 && took < 4000 && (await page.evaluate(() => document.querySelector('#bcv-app h1')?.textContent)) === 'Tools', `found out at the first refused call, no waiting: loaded afresh in ${took} ms, Tools drawn`);
    await page.close();
  }

  // ---- work on the page: a note, then the next page is a real load --------------------------------
  console.log('with text being typed');
  {
    const { page, loads } = await fresh();
    await clearReloadMark(page);
    await page.click('#bcv-omni');
    await page.keyboard.type('kinematics');
    check((await cut('throw')) === 'cut', 'cut off while text is being typed in the search box');
    await page.keyboard.type(' notes'); // (the search asks the extension for Wikipedia: refused)
    await page.waitForSelector('.bcv-toast', { timeout: 8000 }).catch(() => {});
    const note = await page.evaluate(() => [...document.querySelectorAll('.bcv-toast')].map((t) => `${t.textContent} ${t.dataset.code || ''}`).join(' | '));
    check(loads.n === 1 && /Simpl Courses was updated\. Reload the page to continue\./.test(note) && (await page.$eval('#bcv-omni', (e) => e.value)) === 'kinematics notes', `nothing typed is lost: no reload, a note says what happened and what to do (${note})`);
    await page.keyboard.press('Escape'); // (the search's list closed first, the text kept)
    await page.click('#bcv-app .bcv-nav__item[data-nav="todo"]');
    for (let i = 0; i < 40 && loads.n < 2; i++) await page.waitForTimeout(250);
    await page.waitForFunction(() => document.querySelector('#bcv-app h1')?.textContent === 'To Do', null, { timeout: 15000 }).catch(() => {});
    check(loads.n === 2 && (await page.evaluate(() => location.pathname + location.hash)) === '/#todo' && (await page.evaluate(() => document.querySelector('#bcv-app h1')?.textContent)) === 'To Do', `and the next page asked for is loaded for real (To Do, /#todo — the same page's screen is a load all the same), running the new version, rather than drawn by the old one (${loads.n} loads)`);
    await page.close();
  }

  // ---- coming back to the tab: found out before anything is pressed -------------------------------
  console.log('coming back to the tab');
  {
    const { page, loads } = await fresh('/calendar');
    await clearReloadMark(page);
    await page.waitForTimeout(5200); // (the page pinged when it was shown: at most every five seconds)
    check((await cut('hang')) === 'cut', 'cut off while the tab was away (never answered)');
    await page.evaluate(() => { window.dispatchEvent(new Event('focus')); });
    for (let i = 0; i < 40 && loads.n < 2; i++) await page.waitForTimeout(250);
    await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 20000 }).catch(() => {});
    check(loads.n === 2 && (await page.evaluate(() => location.pathname)) === '/calendar' && !!(await page.$('#bcv-app')), 'looked at again, the tab pings the extension, gets no answer, and loads afresh where it was — before anything is pressed');
    await page.close();
  }

  // ---- and a page that is not cut off is left alone --------------------------------------------------
  console.log('not cut off');
  {
    const { page, loads } = await fresh();
    await page.evaluate(() => { window.dispatchEvent(new Event('focus')); });
    await page.click('#bcv-app .bcv-nav__item[data-nav="tools"]');
    await page.waitForFunction(() => document.querySelector('#bcv-app h1')?.textContent === 'Tools', null, { timeout: 15000 }).catch(() => {});
    await page.waitForTimeout(5000);
    const st = await sw.evaluate(async (base) => { const [tab] = await chrome.tabs.query({ url: `${base}/*`, active: true }); const [r] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', func: () => ({ orphaned: self.BCV.life?.orphaned, probe: null }) }); return r?.result; }, BASE);
    const alive = await sw.evaluate(async (base) => { const [tab] = await chrome.tabs.query({ url: `${base}/*`, active: true }); const [r] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', func: async () => self.BCV.life.probe() }); return r?.result; }, BASE);
    check(loads.n === 1 && st?.orphaned === false && alive === true, `a page that is not cut off is never reloaded for this: the ping answers (${JSON.stringify({ ...st, alive })})`);
    await page.close();
  }
} catch (e) {
  console.error('crashed:', e?.stack || e);
  failures.push(`crash: ${e?.message || e}`);
} finally {
  await context.close().catch(() => {});
  server.kill();
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n${failures.map((f) => ` - ${f}`).join('\n')}` : '\nAll checks passed.');
console.log(`(${((Date.now() - t0) / 1000).toFixed(1)}s)`);
process.exit(failures.length ? 1 : 0);

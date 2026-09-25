// The Chrome build finds Canvas on its own.
//
// The Chrome Web Store build (scripts/chrome-manifest.py) may look at any page, and runs one small
// script there (content/sniff.js) that reads the page's markup for Canvas's. This loads that build
// in Chromium — the interface's own scripts matching Canvas's domain alone, as shipped, so a Canvas
// at an address of the school's own is only reached through the sniffer — and checks: the page after
// install says the one thing to do and nothing to press; a page that is not Canvas is left alone,
// even one that borrows a couple of Canvas's ids; the mock Canvas at localhost is found, enabled
// (its scripts registered, the site saved) and loaded again with the setup open on it, and the page
// after install has closed itself by then; a second tab on the site is not loaded again; Canvas's sign-in
// page is enabled but not loaded again.
// Run: node scripts/dev/chrome-setup-test.mjs
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, rmSync } from 'node:fs';
import { createServer } from 'node:http';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const PORT = 8809; // the school's own Canvas (the mock)
const BASE = `http://localhost:${PORT}`;
const OTHER = 8810; // a site that is not Canvas, and a Canvas sign-in page
const OTHER_BASE = `http://localhost:${OTHER}`;
const extDir = join(tmpdir(), `bcv-chrome-ext-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
execSync(`python3 ${JSON.stringify(join(root, 'scripts', 'chrome-manifest.py'))} ${JSON.stringify(join(extDir, 'manifest.json'))}`);
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };

console.log('the Chrome manifest');
const sniffer = manifest.content_scripts.find((cs) => (cs.js || []).includes('content/sniff.js'));
// content/toolbar.js is the other that looks past Canvas: it sleeps on every page until the
// background says this tab is a tool's, and then puts the interface's bar over it
const toolbar = manifest.content_scripts.find((cs) => (cs.js || []).includes('content/toolbar.js'));
// the on-demand modules (content/app/lazy.js) sit in a group whose match never fires: a browser parses none of them until a page asks
const lazy = manifest.content_scripts.find((cs) => (cs.matches || []).includes('https://lazy.simplcourses.invalid/*'));
const own = manifest.content_scripts.filter((cs) => cs !== sniffer && cs !== toolbar && cs !== lazy);
check(JSON.stringify(manifest.host_permissions) === '["*://*/*"]' && !manifest.optional_host_permissions, `the Chrome build has the run of every site, and no optional sites left to ask for: ${JSON.stringify(manifest.host_permissions)}`);
// Held exactly: a permission that brings a new warning switches every installed copy OFF at an update until its owner
// accepts it again (2.98.14 followed one such update). tabs stays out — "Read your browsing history" — since the run of
// every site already lets this build read a tab's address; storage, unlimitedStorage, scripting and activeTab carry no
// warning. A change here is a deliberate one, made knowing what it does to the copies out there.
check(JSON.stringify(manifest.permissions) === '["storage","unlimitedStorage","scripting","activeTab"]', `the Chrome build asks for exactly storage, unlimitedStorage, scripting and activeTab — nothing that reads as browsing history, and nothing new to be accepted at an update: ${JSON.stringify(manifest.permissions)}`);
check(!!sniffer && JSON.stringify(sniffer.matches) === '["*://*/*"]' && JSON.stringify(sniffer.exclude_matches) === '["*://*.instructure.com/*"]' && sniffer.js.length === 1 && sniffer.run_at === 'document_idle', `the sniffer alone runs on every site but Canvas's own, at idle: ${JSON.stringify(sniffer)}`);
check(own.length === 2 && own.every((cs) => JSON.stringify(cs.matches) === '["*://*.instructure.com/*"]'), `the interface's own scripts still match Canvas's domain alone (${own.map((cs) => cs.matches.join(',')).join(' | ')})`);
check(!!lazy && lazy.matches.length === 1 && lazy.js.length >= 10 && lazy.js.includes('content/app/phone.js') && lazy.js.includes('content/app/screens/quiz.js') && !lazy.css, `the on-demand modules keep their never-matching group in the Chrome build (${lazy?.js.length} files)`);
check(!!toolbar && JSON.stringify(toolbar.matches) === '["*://*/*"]' && toolbar.js.length === 1 && toolbar.run_at === 'document_start', `the tool bar runs on every site, early enough to be there before the tool's page paints: ${JSON.stringify(toolbar)}`);
check(!manifest.background.scripts && !('persistent' in manifest.background) && !manifest.author && manifest.action.default_icon['128'] === 'icons/icon-128.png', 'the Firefox/Safari keys are gone and the toolbar icon is the blue tile');

// a site that is not Canvas — with a csrf meta and an #application of its own, as many sites have —
// and, at /login/canvas, a page shaped like Canvas's sign-in page (Canvas's wrapper, no user in ENV)
const other = createServer((req, res) => {
  res.setHeader('content-type', 'text/html; charset=utf-8');
  if (req.url.startsWith('/login/canvas')) {
    res.end('<!doctype html><html><head><meta name="csrf-token" content="x"><script>INST = {}; ENV = {"current_user_id":null,"current_user":{}};</script></head><body class="ic-Login-Body"><div id="application" class="ic-app"><div class="ic-Login"><form id="login_form" action="/login/canvas" method="post"><input name="pseudonym_session[unique_id]"><input type="password" name="pseudonym_session[password]"><button>Log In</button></form></div></div></body></html>');
    return;
  }
  res.end('<!doctype html><html><head><meta name="csrf-token" content="x"><title>Not Canvas</title></head><body><div id="application"><h1>A site of its own</h1><p>With an application div and a csrf meta, as many sites have.</p></div></body></html>');
});
await new Promise((r) => other.listen(OTHER, r));
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));

const userDataDir = join(tmpdir(), `bcv-chrome-profile-${Date.now()}`);
const context = await chromium.launchPersistentContext(userDataDir, { channel: 'chromium', headless: true, viewport: { width: 1400, height: 900 }, args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`] });
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  const isSetup = (p) => p.url().endsWith('/setup/setup.html');
  if (!context.pages().some(isSetup)) await context.waitForEvent('page', { timeout: 8000 }).catch(() => null);
  await new Promise((r) => setTimeout(r, 800));
  const domains = () => sw.evaluate(async () => (await self.BCV.settings.get()).domains);
  const registered = () => sw.evaluate(async () => (await self.BCV.api.scripting.getRegisteredContentScripts()).map((s) => `${s.id}:${s.matches.join(',')}`));

  console.log('the page after install');
  const setup = context.pages().find(isSetup);
  check(!!setup, 'installing opens the page after install');
  await setup.waitForSelector('.splash__stage[data-stage="go"]', { timeout: 8000 });
  await setup.waitForFunction(() => document.querySelectorAll('.splash__stage').length === 1, null, { timeout: 5000 });
  const words = await setup.evaluate(() => ({ title: document.querySelector('.splash__title')?.textContent, hint: document.querySelector('.splash__hint')?.textContent, presses: document.querySelectorAll('button, a').length, note: !!document.querySelector('.splash__note') }));
  check(words.title === 'Open your Canvas' && words.hint === 'Setup will begin there.' && words.presses === 0 && !words.note, `it says the one thing to do, with nothing to press: ${JSON.stringify(words)}`);
  await setup.screenshot({ path: join(root, 'scripts', 'dev', 'out', 'chrome-setup-page.png') });

  console.log('a page that is not Canvas');
  const plain = await context.newPage();
  plain.on('pageerror', (e) => failures.push(`page error: ${e.message}`));
  await plain.goto(`${OTHER_BASE}/`);
  await plain.waitForTimeout(1500);
  check((await domains()).length === 0 && (await registered()).length === 0 && !setup.isClosed(), `is left alone: no site saved, no scripts registered, the page after install still up (${JSON.stringify(await domains())})`);

  console.log("the school's own Canvas");
  const page = await context.newPage();
  page.on('pageerror', (e) => failures.push(`page error: ${e.message}`));
  let loads = 0;
  page.on('load', () => { loads++; });
  const gone = setup.waitForEvent('close', { timeout: 20000 }).then(() => true, () => false);
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-setup', { state: 'attached', timeout: 20000 }); // (a shadow host of no size: attached, not visible)
  for (let i = 0; i < 80 && loads < 3; i++) await page.waitForTimeout(150); // (the card can be up before its own document's load event; a busy machine takes its time over the reload)
  check((await domains()).join(',') === BASE, `found and saved as a site of its own: ${JSON.stringify(await domains())}`);
  const regs = await registered();
  const regFiles = await sw.evaluate(async () => (await self.BCV.api.scripting.getRegisteredContentScripts()).map((s) => s.js.join('+')));
  check(regs.length === 3 && regs.every((r) => r.startsWith('bcv-http---localhost-8809-') && r.endsWith(`:${BASE}/*`)) && regFiles.some((f) => f === 'content/toolbar.js') && !regFiles.some((f) => f.includes('sniff.js')), `the interface's two scripts and the bar over a tool's tab are registered for it, the sniffer not: ${regs.join(' | ')}`);
  check(loads === 3 && page.url() === `${BASE}/`, `the tab loaded again on its own, then once more for the setup's own address, and the setup opened over it (${loads} loads, ${page.url()})`);
  check(await gone, 'and the page after install closed itself once the setup had begun');
  await page.screenshot({ path: join(root, 'scripts', 'dev', 'out', 'chrome-setup-found.png') });

  console.log('a second tab on the site');
  const again = await context.newPage();
  again.on('pageerror', (e) => failures.push(`page error: ${e.message}`));
  let loads2 = 0;
  again.on('load', () => { loads2++; });
  await again.goto(`${BASE}/courses`);
  await again.waitForSelector('#bcv-setup', { state: 'attached', timeout: 20000 });
  for (let i = 0; i < 80 && loads2 < 2; i++) await again.waitForTimeout(150); // (the second load comes on its own time on a busy machine: three suites run side by side)
  await again.waitForTimeout(600); // (and no third one follows)
  check(loads2 === 2 && (await domains()).length === 1, `loads with the interface on it from the start, and once more for the setup's address — never for the sniffer (${loads2} loads)`);

  console.log("Canvas's sign-in page");
  const login = await context.newPage();
  login.on('pageerror', (e) => failures.push(`page error: ${e.message}`));
  let loads3 = 0;
  login.on('load', () => { loads3++; });
  await login.goto(`${OTHER_BASE}/login/canvas`);
  await login.waitForTimeout(1800);
  check((await domains()).includes(OTHER_BASE) && loads3 === 1 && (await login.$('#login_form')) !== null, `is enabled for the page after signing in, and left as it is now (${loads3} loads, ${JSON.stringify(await domains())})`);
} catch (e) {
  console.error('crashed:', e?.stack || e);
  failures.push('crash: ' + e.message);
} finally {
  await context.close();
  rmSync(userDataDir, { recursive: true, force: true });
  server.kill();
  other.close();
  rmSync(extDir, { recursive: true, force: true });
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);

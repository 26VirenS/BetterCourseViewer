// The Mac app's window: the extension's settings page over the app's bridge, in a browser.
//
// ViewController.swift loads options/options.html from the extension inside the app and injects
// Resources/Bridge.js first, which stands in for the browser APIs with messages to the app. Here
// the page is served over http, the real Bridge.js goes in as an init script, and a fake app answers
// its messages (and records them). Checks: the sections the window shows (This Mac first, no
// Courses & targets or Grades — those need the browser's Canvas session); Safari's word and the
// update states as the app pushes them; the buttons reaching the app; a setting written to the
// store, and a change the extension made arriving; a site added; export, import and reset through
// the app's panels; the Mac's uninstall steps.
// Run: node scripts/dev/mac-window-test.mjs
import { createRequire } from 'node:module';
import { createServer } from 'node:http';
import { execSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join, normalize } from 'node:path';

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const prefix = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(prefix, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const EXT = join(root, 'extension');
const BRIDGE = readFileSync(join(root, 'macos/Simpl Courses/Simpl Courses/Resources/Bridge.js'), 'utf8').replace('__MANIFEST__', JSON.stringify({ version: '9.9.9', name: 'Simpl Courses' }));
const TYPES = { '.html': 'text/html', '.css': 'text/css', '.js': 'text/javascript', '.png': 'image/png', '.json': 'application/json' };
const server = createServer((req, res) => {
  const path = join(EXT, normalize(decodeURIComponent(req.url.split('?')[0])));
  if (!path.startsWith(EXT)) { res.writeHead(403).end(); return; }
  try {
    const body = readFileSync(path);
    res.writeHead(200, { 'content-type': TYPES[path.slice(path.lastIndexOf('.'))] || 'application/octet-stream' }).end(body);
  } catch {
    res.writeHead(404).end();
  }
});
await new Promise((r) => server.listen(0, r));
const BASE = `http://localhost:${server.address().port}`;

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };

// the fake app: the shared store, the app's state, and every message the page sends
const FAKE_APP = () => {
  const store = { settings: null, commands: [], site: { host: 'canvas.school.test', origin: 'https://canvas.school.test' }, setupDone: true };
  window.__store = store;
  window.__calls = [];
  window.__appState = { version: '9.9.9', extension: { state: 'on', detail: '' }, update: { state: 'upToDate', checked: 1700000000, automatic: true, version: '9.9.9', feed: 'https://simplcourses.com/app/latest.json' }, loginItem: true, setupDone: true, site: store.site };
  const all = () => { const o = {}; if (store.settings) o.settings = store.settings; if (store.site) o['site:last'] = store.site; if (store.setupDone) o['setup:done'] = true; return o; };
  const clone = (v) => JSON.parse(JSON.stringify(v));
  window.webkit = { messageHandlers: { simpl: { postMessage: async (m) => {
    window.__calls.push(clone(m));
    switch (m.cmd) {
      case 'storage.get': {
        const a = all(); const k = m.keys;
        if (k == null) return a;
        if (typeof k === 'string') return k in a ? { [k]: a[k] } : {};
        if (Array.isArray(k)) { const o = {}; for (const x of k) if (x in a) o[x] = a[x]; return o; }
        const o = {}; for (const [x, d] of Object.entries(k)) o[x] = x in a ? a[x] : d; return o;
      }
      case 'storage.set': if (m.items && m.items.settings) store.settings = clone(m.items.settings); return { ok: true };
      case 'storage.remove': if ((m.keys || []).includes('settings')) store.settings = null; return { ok: true };
      case 'storage.clear': store.settings = null; store.commands.push({ id: 'w1', type: 'wipe' }); return { ok: true };
      case 'runtime.sendMessage': {
        const t = m.message && m.message.type;
        if (t === 'registerDomain') { const s = store.settings || {}; s.domains = [...(s.domains || []).filter((d) => d !== m.message.origin), m.message.origin]; store.settings = s; return { ok: true, origin: m.message.origin, message: `Enabled on ${m.message.origin}. Safari asks for the site when you open it.` }; }
        if (t === 'unregisterDomain') { const s = store.settings || {}; s.domains = (s.domains || []).filter((d) => d !== m.message.origin); store.settings = s; return { ok: true }; }
        return { ok: true };
      }
      case 'app.state': return window.__appState;
      case 'app.releases': return { ok: true, releases: [{ version: '9.9.9', url: 'https://example.test/Simpl-Courses-Mac-9.9.9.zip', sha256: 'abc' }, { version: '9.9.8', url: 'https://example.test/Simpl-Courses-Mac-9.9.8.zip', sha256: 'def' }, { version: '9.9.7', url: 'https://example.test/Simpl-Courses-Mac-9.9.7.zip' }] };
      case 'app.rollback': return { ok: true };
      case 'app.openSafariSettings': return { ok: false, message: 'Safari is not running.' };
      case 'app.moveToApplications': return { ok: false, message: 'The Applications folder cannot be written. Drag the app to the Applications folder in the Finder, then open it again.' };
      case 'file.save': return { ok: true, path: '/tmp/settings.json' };
      case 'file.open': return { ok: true, name: 'settings.json', text: JSON.stringify({ appearance: { darkMode: 'on', sideCourses: 'hover' } }) };
      default: return { ok: true };
    }
  } } } };
};

const browser = await chromium.launch({ channel: 'chromium', headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 960, height: 660 } });
  page.on('pageerror', (e) => { console.log('  page error:', e.message); failures.push(`page error: ${e.message}`); });
  page.on('dialog', (d) => d.accept());
  await page.addInitScript(FAKE_APP);
  await page.addInitScript(BRIDGE);
  await page.goto(`${BASE}/options/options.html`);
  await page.waitForSelector('.navlink', { timeout: 8000 });
  await page.waitForTimeout(300);
  const text = (sel) => page.$eval(sel, (el) => (el.innerText || el.textContent).replace(/\s+/g, ' ').trim()).catch(() => null);
  const texts = (sel) => page.$$eval(sel, (els) => els.map((el) => (el.innerText || el.textContent).replace(/\s+/g, ' ').trim()));
  const calls = (cmd) => page.evaluate((c) => window.__calls.filter((m) => m.cmd === c), cmd);
  const push = (patch) => page.evaluate((p) => { window.__appState = { ...window.__appState, ...p }; window.__simplApp(window.__appState); }, patch);
  const flashed = async () => { const t = await text('#savedText'); const hidden = await page.$eval('#saved', (e) => e.hidden); return hidden ? '' : t; };

  console.log('the window');
  const nav = await texts('.navlink');
  check(nav.join(' | ') === 'This Mac | General | Appearance | Canvas sites | Data & about', `the sections the app's window shows, This Mac first, with no Courses & targets or Grades (those live on Canvas's pages): ${nav.join(' | ')}`);
  check((await page.$eval('.section.is-active', (e) => e.id)) === 'app' && (await text('#title')) === 'This Mac' && (await text('#version')) === 'Version 9.9.9', `it opens on This Mac, with the app's version in the corner (${await text('#title')}, ${await text('#version')})`);
  check((await text('#extTitle')) === 'Simpl Courses is on in Safari' && /Settings → Extensions/.test(await text('#extSub')) && (await page.$eval('#extSteps', (e) => e.hidden)), `Safari's word, from the app's state: ${await text('#extTitle')}`);
  check((await text('#updTitle')) === 'Version 9.9.9 is the newest' && /Checked at .* · checks every hour/.test(await text('#updSub')) && (await page.$eval('#updAction', (e) => e.hidden)) && (await page.$eval('#autoUpdate', (e) => e.classList.contains('is-on'))) && (await page.$eval('#loginItem', (e) => e.classList.contains('is-on'))), `the update line: the newest, checked at a time, every hour; both switches on (${await text('#updTitle')} · ${await text('#updSub')})`);
  await page.screenshot({ path: join(root, 'scripts', 'dev', 'out', 'mac-window-app.png') });

  console.log("Safari's states and the update's");
  await push({ extension: { state: 'off', detail: '' } });
  check((await text('#extTitle')) === 'Simpl Courses is off in Safari' && /Tick it under Safari/.test(await text('#extSub')), `off: ${await text('#extTitle')}`);
  await push({ extension: { state: 'missing', detail: 'Safari could not find it.' } });
  const steps = await page.$$eval('#extSteps li', (els) => els.map((e) => e.innerText.replace(/\s+/g, ' ').trim()));
  check((await text('#extTitle')) === 'Safari does not have the extension yet' && (await text('#extSub')) === 'Safari could not find it.' && !(await page.$eval('#extSteps', (e) => e.hidden)) && steps.length === 3 && /Applications/.test(steps[0]) && /Allow unsigned extensions/.test(steps[2]) && (await page.$eval('#moveApp', (e) => e.hidden)) && !(await page.$eval('#openSafari', (e) => e.hidden)), `not known to Safari: Safari's own message and the three steps that put it back (${steps.map((s) => s.slice(0, 30)).join(' | ')})`);
  await push({ extension: { state: 'missing', detail: 'macOS is running this copy from a temporary place, so Safari cannot see the extension inside it. Move the app to the Applications folder.' }, placement: { translocated: true, inApplications: false, path: '/Users/me/Downloads/Simpl Courses.app' } });
  check(!(await page.$eval('#moveApp', (e) => e.hidden)) && (await page.$eval('#openSafari', (e) => e.hidden)) && /temporary place/.test(await text('#extSub')), `opened from Downloads (macOS running a temporary copy): the button is Move to Applications, and the line says why (${await text('#extSub')})`);
  await page.screenshot({ path: join(root, 'scripts', 'dev', 'out', 'mac-window-move.png') });
  await page.click('#moveApp');
  await page.waitForTimeout(200);
  check((await calls('app.moveToApplications')).length === 1 && /cannot be written\. Drag the app to the Applications folder/.test(await text('#appMsg')) && !(await page.$eval('#appMsg', (e) => e.hidden)), `Move to Applications reaches the app, and a refusal is said under the button: ${await text('#appMsg')}`);
  await push({ extension: { state: 'missing', detail: 'Safari has not registered the extension yet. Open Safari once, then come back here.' }, placement: { translocated: false, inApplications: true, path: '/Applications/Simpl Courses.app' } });
  check((await page.$eval('#moveApp', (e) => e.hidden)) && !(await page.$eval('#openSafari', (e) => e.hidden)), 'in the Applications folder, the button is Open Safari Settings again');
  await page.evaluate(() => { document.getElementById('appMsg').hidden = true; });
  await push({ extension: { state: 'on', detail: '' }, update: { state: 'available', available: '9.10.0', automatic: true, checked: 1700000000, version: '9.9.9' } });
  check((await text('#updTitle')) === 'Version 9.10.0 is ready' && /You have 9\.9\.9\. It installs by itself in a moment\./.test(await text('#updSub')) && !(await page.$eval('#updAction', (e) => e.hidden)), `an update found: ${await text('#updTitle')} · ${await text('#updSub')}`);
  await push({ update: { state: 'downloading', progress: 0.42, automatic: true } });
  check((await text('#updTitle')) === 'Downloading the update…' && (await text('#updSub')) === '42%' && (await page.$eval('#checkUpdates', (e) => e.disabled)), `downloading, with the progress: ${await text('#updSub')}`);
  await page.screenshot({ path: join(root, 'scripts', 'dev', 'out', 'mac-window-update.png') });
  await push({ update: { state: 'installing', automatic: true } });
  check((await text('#updTitle')) === 'Installing…' && /opens again by itself/.test(await text('#updSub')), 'installing: the app opens again by itself');
  await push({ update: { state: 'failed', message: 'The update feed could not be read.', automatic: false, checked: 1700000000 } });
  check((await text('#updTitle')) === 'Could not check for updates' && (await text('#updSub')) === 'The update feed could not be read.' && !(await page.$eval('#autoUpdate', (e) => e.classList.contains('is-on'))) && !(await page.$eval('#checkUpdates', (e) => e.disabled)), 'a failed check says why, and Check now is back');
  await push({ update: { state: 'available', available: '9.10.0', automatic: false, checked: 1700000000 } });

  console.log('the buttons reach the app');
  await page.click('#checkUpdates');
  await page.click('#updAction');
  await page.click('#autoUpdate');
  await page.click('#loginItem');
  await page.click('#openSafari');
  await page.waitForTimeout(200);
  const sent = await page.evaluate(() => window.__calls.filter((m) => m.cmd.startsWith('app.') && m.cmd !== 'app.state').map((m) => `${m.cmd}${'on' in m ? `:${m.on}` : ''}`));
  check(sent.join(',') === 'app.moveToApplications,app.checkUpdates,app.installUpdate,app.setAutoUpdate:true,app.setLoginItem:false,app.openSafariSettings', `Check now, Update now, the two switches and Open Safari Settings each reach the app: ${sent.join(',')}`);
  check(/Safari is not running\. Open Safari, then Safari → Settings → Extensions/.test(await text('#appMsg')) && !(await page.$eval('#appMsg', (e) => e.hidden)), `Safari refusing to open its settings is said under the button: ${await text('#appMsg')}`);

  console.log('a setting, both ways');
  await page.click('.navlink[data-section="general"]');
  await page.click('#skin');
  await page.waitForTimeout(250);
  const sets = await calls('storage.set');
  check(sets.length === 1 && sets[0].items.settings.appearance.skin === false && (await calls('runtime.sendMessage')).some((m) => m.message.type === 'pushSettings') && (await flashed()) === 'Saved', `the look switch writes the settings to the app's store (and asks for them pushed): skin ${sets[0]?.items.settings.appearance.skin}`);
  await page.evaluate(() => { window.__store.settings = { appearance: { skin: true, darkMode: 'on' } }; window.__simplStorageChanged({ settings: { newValue: window.__store.settings } }); }); // (the store changed under the page, as the app reports)
  await page.waitForTimeout(200);
  check((await page.$eval('#skin', (e) => e.classList.contains('is-on'))) && (await page.$eval('html', (e) => e.getAttribute('data-theme'))) === 'dark', 'a change the extension made (the look switch on a page, dark) arrives through the store and repaints the page');
  await page.click('.navlink[data-section="appearance"]');
  check((await page.$eval('.theme.is-on', (e) => e.dataset.value)) === 'on', 'and Appearance shows Dark');
  await page.click('#runWelcome').catch(() => {});
  await page.click('.navlink[data-section="general"]');
  await page.click('#runWelcome');
  await page.waitForTimeout(150);
  const opened = await calls('open');
  check(opened.length === 1 && opened[0].url === 'https://canvas.school.test/?bcv=welcome', `See it again opens the welcome on the Canvas the extension last drew, in Safari: ${opened[0]?.url}`);

  console.log('a site added from the app');
  await page.click('.navlink[data-section="sites"]');
  await page.fill('#newDomain', 'canvas.school.edu');
  await page.click('#addDomain');
  await page.waitForTimeout(250);
  const hosts = await texts('#domains .site__host');
  check(hosts.join(',') === '*.instructure.com,canvas.school.edu' && /Enabled on https:\/\/canvas\.school\.edu\. Safari asks for the site when you open it\./.test(await text('#domainMsg')), `the site is on the list, with the app's word on what happens next: ${await text('#domainMsg')}`);

  console.log('data & about');
  await page.click('.navlink[data-section="data"]');
  await page.waitForTimeout(200);
  const stats = await texts('#dataStats .stat');
  check(stats.length === 4 && /Settings kept in the app, shared with Safari/.test(stats[0]) && /Extension in Safari on/.test(stats[2]), `the app's own stats: ${stats.join(' | ')}`);
  const unin = await page.$$eval('#uninstallSteps li', (els) => els.map((e) => e.innerText));
  check(unin.length === 2 && /Applications folder/.test(unin[1]) && /Mac/.test(await text('#uninstallSub')), `the uninstall steps are the Mac's: ${unin[1]?.slice(0, 60)}`);
  await page.click('#exportSettings');
  await page.waitForTimeout(200);
  const saved = await calls('file.save');
  check(saved.length === 1 && saved[0].name === 'simpl-courses-settings.json' && JSON.parse(saved[0].text).appearance.darkMode === 'on' && (await flashed()) === 'Saved', 'Export settings goes through the app\'s save panel');
  await page.click('#importSettings');
  await page.waitForTimeout(250);
  check((await calls('file.open')).length === 1 && (await flashed()) === 'Imported' && (await page.evaluate(() => window.__store.settings.appearance.sideCourses)) === 'hover', 'Import settings goes through the app\'s open panel and lands in the store');
  await page.click('#resetSettings');
  await page.waitForTimeout(300);
  check((await calls('storage.clear')).length === 1 && (await page.evaluate(() => window.__store.commands.map((c) => c.type).join(','))) === 'wipe' && (await calls('runtime.sendMessage')).some((m) => m.message.type === 'wipeSiteNotes') && (await flashed()) === 'Reset', 'Reset everything clears the store, which leaves the extension a wipe to do');
  await page.screenshot({ path: join(root, 'scripts', 'dev', 'out', 'mac-window-data.png') });

  // ---- Developer: the rollback (the app's window alone) — the releases listed, one picked, installed in this copy's place
  console.log('developer: roll back');
  for (let i = 0; i < 5; i++) await page.click('#version');
  await page.waitForSelector('#dev:not([hidden])', { timeout: 5000 });
  await page.waitForFunction(() => document.querySelectorAll('#devVersions option').length === 3, null, { timeout: 5000 });
  const roll = await page.evaluate(() => ({ card: !document.getElementById('devRoll').hidden, options: [...document.querySelectorAll('#devVersions option')].map((o) => o.textContent).join(','), sub: document.getElementById('devRollSub').textContent }));
  check(roll.card && roll.options === '9.9.9 (this one),9.9.8,9.9.7' && /Automatic updates go off/.test(roll.sub), `the Developer section lists the versions published, this one marked: ${JSON.stringify(roll)}`);
  await page.selectOption('#devVersions', '9.9.8');
  await page.click('#devRollback');
  await page.waitForTimeout(300);
  const rolled = await calls('app.rollback');
  check(rolled.length === 1 && rolled[0].version === '9.9.8' && rolled[0].url === 'https://example.test/Simpl-Courses-Mac-9.9.8.zip' && rolled[0].sha256 === 'def' && (await text('#devRollMsg')) === 'Installing 9.9.8: the app relaunches once it is in place.', `Install asks the app for that version, its zip and its checksum, and says so: ${JSON.stringify(rolled)}`);
  await page.screenshot({ path: join(root, 'scripts', 'dev', 'out', 'mac-window-rollback.png') });
} catch (e) {
  console.error('mac window test crashed:', e?.stack || e);
  failures.push('crash: ' + e.message);
} finally {
  await browser.close();
  server.close();
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);

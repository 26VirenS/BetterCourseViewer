// The Mac app's own window (Resources/Base.lproj/Main.html + Style.css + Script.js), in a browser.
// ViewController.swift calls show(state, modernNames, detail); everything else here is the page.
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
const RES = join(root, 'macos/Simpl Courses/Simpl Courses/Resources');
const TYPES = { '.html': 'text/html', '.css': 'text/css', '.js': 'text/javascript', '.png': 'image/png' };
// the page is loaded over http so its own Content-Security-Policy ('self') behaves as it does in the app
const server = createServer((req, res) => {
  const path = join(RES, normalize(decodeURIComponent(req.url.split('?')[0])));
  if (!path.startsWith(RES)) { res.writeHead(403).end(); return; }
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

const browser = await chromium.launch({ channel: 'chromium', headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 700, height: 640 } });
  const posted = [];
  await page.exposeFunction('__post', (m) => posted.push(m));
  await page.addInitScript(() => { window.webkit = { messageHandlers: { controller: { postMessage: (m) => window.__post(m) } } }; });
  page.on('pageerror', (e) => console.log('  page error:', e.message));
  await page.goto(`${BASE}/Base.lproj/Main.html`);
  await page.waitForFunction(() => typeof window.show === 'function', null, { timeout: 5000 });

  const shown = () => page.evaluate(() => [...document.querySelectorAll('body > *')]
    .filter((el) => el.getBoundingClientRect().height > 0)
    .map((el) => el.id || el.tagName.toLowerCase()));
  const text = (sel) => page.$eval(sel, (el) => el.innerText.replace(/\s+/g, ' ').trim()).catch(() => null);

  // Before Safari has answered: the window still has to be a window (this is what a hidden body broke)
  const first = await shown();
  check(await page.$eval('body', (el) => getComputedStyle(el).display !== 'none' && el.getBoundingClientRect().height > 0), 'the window is not blank before Safari answers');
  check(first.includes('img') && first.includes('line-unknown') && first.includes('open-preferences') && first.includes('uninstall'), `it waits with the icon, a line and both buttons: ${first.join(', ')}`);

  await page.evaluate(() => window.show('on', true, ''));
  const on = await shown();
  check(on.includes('line-on') && !on.includes('line-off') && !on.includes('line-missing') && !on.includes('steps'), `extension on: only that line (${on.join(', ')})`);
  check(/Extensions section of Safari Settings/.test(await text('.state-on')) && (await text('button.open-preferences')) === 'Quit and Open Safari Settings…', 'macOS 13 and later: Settings, not Preferences');

  await page.reload();
  await page.evaluate(() => window.show('off', false, ''));
  check((await shown()).includes('line-off') && /Safari Extensions preferences/.test(await text('.state-off')) && (await text('button.open-preferences')) === 'Quit and Open Safari Extensions Preferences…', 'older macOS keeps the Preferences wording');

  await page.evaluate(() => window.show('missing', true, 'Safari could not find it.'));
  const missing = await shown();
  const steps = await page.$$eval('.steps li', (els) => els.map((e) => e.innerText.replace(/\s+/g, ' ').trim()));
  check(missing.includes('line-missing') && missing.includes('steps') && missing.includes('detail') && !missing.includes('line-on') && !missing.includes('line-off'), `not known to Safari: the explanation and the steps (${missing.join(', ')})`);
  check(steps.length === 3 && /Applications/.test(steps[0]) && /Settings → Extensions/.test(steps[1]) && /Allow unsigned extensions/.test(steps[2]) && /turns itself off/.test(steps[2]), `the steps name what puts it back: ${steps.map((s) => s.slice(0, 40)).join(' | ')}`);
  check((await text('.detail')) === 'Safari could not find it.', "Safari's own message is shown underneath");

  await page.evaluate(() => window.show('on', true, ''));
  check(!(await shown()).includes('detail'), 'the message goes away once the extension is found');

  // the buttons talk to the app
  await page.click('button.open-preferences');
  await page.click('button.uninstall');
  check(posted.join(',') === 'open-preferences,uninstall', `both buttons reach the app: ${posted.join(',') || 'nothing'}`);
} catch (e) {
  console.error('mac window test crashed:', e?.stack || e);
  failures.push('crash: ' + e.message);
} finally {
  await browser.close();
  server.close();
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);

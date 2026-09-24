#!/usr/bin/env node
// Checks the iOS app's injection model without an iPhone: the extension is injected into a plain
// page the way ios/SimplCourses/Web/ScriptBundle.swift does it (bridge first, then background, then
// the manifest's content scripts at their run_at, then the stylesheet), against the mock Canvas in
// headless Chromium. No extension runtime exists here, so the bridge's fallback paths are exercised:
// storage on localStorage and the in-page message bus.
// Requires Playwright (project or global install).
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
let chromium;
try {
  ({ chromium } = require('playwright'));
} catch {
  const prefix = execSync('npm root -g').toString().trim();
  ({ chromium } = createRequire(join(prefix, 'x.js'))('playwright'));
}

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const ext = join(root, 'extension');
const PORT = 8790;
const BASE = `http://localhost:${PORT}`;
const HOST = 'localhost';

// ---- the bundle, built the way the app builds it --------------------------------------------
const manifest = JSON.parse(readFileSync(join(ext, 'manifest.json'), 'utf8'));
const file = (rel) => readFileSync(join(ext, rel), 'utf8');
const guardJS = `if(location.hostname!==${JSON.stringify(HOST)})return;`;
const seen = new Set();
const wrap = (rel) => {
  if (seen.has(rel)) return '';
  seen.add(rel);
  return `(function(){${guardJS}try{\n${file(rel)}\n}catch(e){console.error('[Simpl Courses] ${rel} failed',e)}})();\n`;
};
const bridge = readFileSync(join(root, 'ios', 'SimplCourses', 'Web', 'bridge.js'), 'utf8')
  .split('__MANIFEST__').join(JSON.stringify(manifest)); // every occurrence, as the app does
const start = [`(function(){${guardJS}\n${bridge}\n})();\n`];
const end = [];
// the background script and what it needs, as the manifest lists them (the app reads the same list)
for (const rel of manifest.background?.scripts || ['lib/settings.js', 'lib/devcode.js', 'background.js']) start.push(wrap(rel));
const css = [];
for (const cs of manifest.content_scripts || []) {
  if ((cs.js || []).includes('content/sniff.js')) continue; // (the browsers' finder of a school's Canvas: the app chooses the school natively, as ScriptBundle.swift skips it)
  const list = cs.run_at === 'document_start' ? start : end;
  for (const rel of cs.js || []) list.push(wrap(rel));
  for (const rel of cs.css || []) css.push(file(rel));
}
start.splice(1, 0, `(function(){${guardJS}var s=document.createElement('style');s.id='bcv-css';s.textContent=${JSON.stringify(css.join('\n'))};(document.head||document.documentElement).appendChild(s);})();\n`);
end.push('(function(){var s=document.getElementById(\'bcv-css\');if(s&&document.body)document.body.appendChild(s);})();\n');
// Playwright has one injection point, and it fires before the <html> element exists, whereas WebKit's
// document-start scripts run right after it is created: wait for it, as the app's timing has it.
// The document_end scripts wait for DOMContentLoaded.
const initScript = `(function () {
  function start() {\n${start.join('\n')}\n}
  if (document.documentElement) start();
  else new MutationObserver(function (m, o) { if (document.documentElement) { o.disconnect(); start(); } }).observe(document, { childList: true });
  document.addEventListener('DOMContentLoaded', function () {\n${end.join('\n')}\n});
})();`;

// ---- run ------------------------------------------------------------------------------------
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));
const failures = [];
const check = (cond, label) => {
  console.log(`${cond ? '  ✓' : '  ✗'} ${label}`);
  if (!cond) failures.push(label);
};
const browser = await chromium.launch({ channel: 'chromium' });
const context = await browser.newContext({ viewport: { width: 1200, height: 800 } });
// a fresh profile would open the guided setup, What's New, the search welcome and the Tools welcome first (the suites seed
// the same marks); the setup's flow number comes from background.js, whose migration clears the marks of an older flow
const setupFlow = Number((file('background.js').match(/const SETUP_FLOW = (\d+)/) || [])[1]) || 0;
await context.addInitScript(({ v, flow }) => { try { if (!localStorage.getItem('bcv:storage')) localStorage.setItem('bcv:storage', JSON.stringify({ 'setup:flow': flow, 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'tools:welcomed': true, 'whatsnew:seen': v })); } catch { /* ignore */ } }, { v: manifest.version, flow: setupFlow });
await context.addInitScript(initScript);
let page = null;
const errors = [];
try {
  page = await context.newPage();
  page.on('pageerror', (e) => errors.push(e.message));
  page.on('console', (m) => { if (m.type() === 'error') errors.push(m.text()); });
  const texts = (sel) => page.$$eval(sel, (els) => els.map((e) => (e.innerText || e.textContent).replace(/\s+/g, ' ').trim()));

  console.log('injected bundle');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 15000 });
  check(await page.$eval('html', (h) => h.classList.contains('bcv-on')) && (await page.$('#bcv-app')) !== null, 'the interface mounts from injected scripts (no extension runtime)');
  check((await texts('.bcv-stat')).length === 6 && (await texts('.bcv-nav__item')).some((t) => /Dashboard/.test(t)), 'dashboard data loads through the page\'s own Canvas session');
  const api = await page.evaluate(async () => {
    const m = browser.runtime.getManifest();
    const status = await browser.runtime.sendMessage({ type: 'devGet' }); // the Developer section's read of the tool-tab settings: answered by background.js alone
    const none = await browser.runtime.sendMessage({ type: 'nobody-handles-this' });
    return { name: m.name, version: m.version, status, none };
  });
  check(api.name === 'Simpl Courses' && /^\d+\.\d+\.\d+$/.test(api.version), `runtime.getManifest comes from the bundled manifest: ${api.name} ${api.version}`);
  check(api.status && api.status.ok === true && api.status.normal === true && typeof api.status.code === 'string' && api.none === undefined, `one-shot messages reach background.js inside the page: ${JSON.stringify(api.status)}`);
  check(errors.length === 0, `no page errors while mounting${errors.length ? `: ${errors.slice(0, 3).join(' | ')}` : ''}`);

  console.log('storage + onChanged');
  await page.evaluate(() => browser.storage.local.set({ settings: { appearance: { darkMode: 'on' } } }));
  await page.waitForFunction(() => document.documentElement.getAttribute('data-bcv-theme') === 'dark', null, { timeout: 5000 });
  check(true, 'a storage write fires storage.onChanged: early.js switches to dark');
  await page.reload();
  await page.waitForSelector('.bcv-stat', { timeout: 15000 });
  check((await page.$eval('html', (h) => h.getAttribute('data-bcv-theme'))) === 'dark', 'storage persists across a page load');
  const got = await page.evaluate(async () => {
    const one = await browser.storage.local.get('settings');
    const withDefault = await browser.storage.local.get({ missing: 7 });
    await browser.storage.local.remove('scratch');
    return { dark: one.settings?.appearance?.darkMode, def: withDefault.missing };
  });
  check(got.dark === 'on' && got.def === 7, 'storage.get supports a key and {key: default}');
  await page.evaluate(() => browser.storage.local.set({ settings: { appearance: { darkMode: 'off' } } }));
  await page.waitForFunction(() => document.documentElement.getAttribute('data-bcv-theme') === 'light', null, { timeout: 5000 });

  console.log('navigation');
  await page.goto(`${BASE}/courses/101`);
  await page.waitForSelector('.bcv-front', { timeout: 15000 });
  check((await texts('.bcv-rail__item')).some((t) => /Grades/.test(t)), 'a course page renders from the injected bundle');
  await page.goto(`${BASE}/courses/101/quizzes/9001?bcv=feedback&sub=qs1`);
  await page.waitForSelector('.bcv-fb__q', { timeout: 15000 });
  check((await page.$$('.bcv-fb__q')).length === 4, 'the quiz feedback screen renders too');
  check(errors.length === 0, `still no page errors${errors.length ? `: ${errors.slice(0, 3).join(' | ')}` : ''}`);

  console.log('host guard');
  const other = await context.newPage();
  await other.goto(`http://127.0.0.1:${PORT}/`);
  await other.waitForSelector('#header', { timeout: 15000 });
  check(!(await other.$('#bcv-app')) && !(await other.evaluate(() => document.documentElement.classList.contains('bcv-on'))), 'a page on another host (a single sign-on page) is left untouched');
  await other.close();
} catch (e) {
  console.error('hybrid test crashed:', e);
  failures.push(`crash: ${e.message}`);
  // what the page was showing when it happened, so a stuck screen can be read from the log alone
  const state = await page?.evaluate(() => ({
    url: location.href, html: document.documentElement.className, h1: document.querySelector('h1')?.textContent?.trim() || null,
    main: [...document.querySelectorAll('#bcv-main > *')].map((e) => e.className).join(' | ') || null,
    rail: [...document.querySelectorAll('.bcv-rail__item')].length, toasts: [...document.querySelectorAll('.bcv-toast')].map((e) => e.textContent.trim()),
    over: [...document.body.children].filter((e) => e.id !== 'bcv-app').map((e) => e.id || e.className || e.tagName).slice(0, 12),
    storage: (() => { try { return Object.keys(JSON.parse(localStorage.getItem('bcv:storage') || '{}')); } catch { return null; } })(),
  })).catch((err) => `unreadable: ${err.message}`);
  console.error('page state at the crash:', JSON.stringify(state), 'errors:', JSON.stringify(errors.slice(0, 5)));
} finally {
  await browser.close();
  server.kill();
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);

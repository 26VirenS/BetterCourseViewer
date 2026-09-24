#!/usr/bin/env node
// Measures what the interface costs: the JS heap, DOM nodes, listeners, layout and style work,
// requests to Canvas, and the scripts a Canvas page parses — screen by screen, and over an idle
// minute on the Dashboard (a heap that keeps climbing while nothing happens is a leak or a poll).
// Loads the extension into headless Chromium against the mock, like the smoke suite, but touches
// nothing and checks nothing: it prints a table and writes scripts/dev/out/measure-<label>.json so
// two runs can be compared (`node scripts/dev/measure.mjs before`, later `… after`).
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { mkdirSync, cpSync, readFileSync, writeFileSync, rmSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { shortenTimers, afterMigration } from './harness.mjs';

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const prefix = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(prefix, 'x.js'))('playwright')); }

const label = process.argv[2] || 'now';
const IDLE_S = Number(process.argv[3] || 40); // seconds to sit on the Dashboard sampling the heap
const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const out = join(root, 'scripts', 'dev', 'out');
mkdirSync(out, { recursive: true });
const PORT = 8805, SIM_PORT = 8806;
const BASE = `http://localhost:${PORT}`;

const extDir = join(tmpdir(), `bcv-measure-${process.pid}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) cs.matches.push(`${BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
shortenTimers(extDir);
// the bytes a Canvas page parses on every load: each content script group that matches Canvas, in manifest order
const groups = manifest.content_scripts.filter((cs) => cs.matches.some((m) => /instructure|<all_urls>|\*:\/\/\*\/\*/.test(m))).map((cs) => ({ run_at: cs.run_at || 'document_idle', js: (cs.js || []).map((f) => ({ f, bytes: statSync(join(extDir, f)).size })), css: (cs.css || []).map((f) => ({ f, bytes: statSync(join(extDir, f)).size })) }));
const parsed = { js: groups.flatMap((g) => g.js).reduce((s, x) => s + x.bytes, 0), css: groups.flatMap((g) => g.css).reduce((s, x) => s + x.bytes, 0), files: groups.flatMap((g) => [...g.js, ...g.css]).length };

const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT), String(SIM_PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));
const userDataDir = join(tmpdir(), `bcv-measure-profile-${process.pid}`);
const context = await chromium.launchPersistentContext(userDataDir, { channel: 'chromium', headless: true, viewport: { width: 1400, height: 900 }, args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`, '--js-flags=--expose-gc'] });
const result = { label, at: new Date().toISOString(), version: manifest.version, parsed, screens: [], idle: [], storage: null, errors: [] };
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  await afterMigration(sw);
  await new Promise((r) => setTimeout(r, 800));
  for (const p of context.pages()) if (p.url().endsWith('/setup/setup.html')) await p.close();
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'tools:welcomed': true, 'whatsnew:seen': v }), manifest.version);
  const page = await context.newPage();
  page.on('pageerror', (e) => result.errors.push(e.message));
  let reqs = 0; let reqBytes = 0;
  page.on('request', (r) => { if (r.url().startsWith(BASE) && r.url().includes('/api/')) reqs += 1; });
  page.on('response', async (r) => { try { if (r.url().startsWith(BASE) && r.url().includes('/api/')) reqBytes += Number(r.headers()['content-length'] || 0); } catch { /* gone */ } });
  const cdp = await context.newCDPSession(page);
  await cdp.send('Performance.enable');
  await cdp.send('HeapProfiler.enable');
  const settled = () => page.waitForFunction(() => { const c = document.documentElement.classList; return !c.contains('bcv-on') || c.contains('bcv-settled'); }, null, { timeout: 20000 }).catch(() => {});
  const metrics = async () => {
    await cdp.send('HeapProfiler.collectGarbage').catch(() => {});
    await new Promise((r) => setTimeout(r, 300));
    const m = Object.fromEntries((await cdp.send('Performance.getMetrics')).metrics.map((x) => [x.name, x.value]));
    const dom = await page.evaluate(() => ({ nodes: document.getElementsByTagName('*').length, appNodes: document.getElementById('bcv-app')?.getElementsByTagName('*').length || 0, hidden: [...document.querySelectorAll('#bcv-app [hidden], #bcv-app .is-hidden')].length, imgs: document.images.length, styles: document.styleSheets.length }));
    return { heapMB: +(m.JSHeapUsedSize / 1048576).toFixed(2), heapTotalMB: +(m.JSHeapTotalSize / 1048576).toFixed(2), nodes: m.Nodes, listeners: m.JSEventListeners, documents: m.Documents, frames: m.Frames, layouts: m.LayoutCount, restyles: m.RecalcStyleCount, scriptS: +m.ScriptDuration.toFixed(2), taskS: +m.TaskDuration.toFixed(2), ...dom };
  };
  const measure = async (name) => { const m = await metrics(); result.screens.push({ name, reqs, reqBytes, ...m }); console.log(`  ${name.padEnd(22)} heap ${String(m.heapMB).padStart(6)} MB  nodes ${String(m.nodes).padStart(5)}  app ${String(m.appNodes).padStart(5)}  listeners ${String(m.listeners).padStart(4)}  layouts ${String(m.layouts).padStart(4)}  restyles ${String(m.restyles).padStart(4)}  script ${m.scriptS}s  api reqs ${reqs}`); reqs = 0; reqBytes = 0; };
  console.log(`measure: ${label} (v${manifest.version}) — ${parsed.files} files parsed on every Canvas page: ${(parsed.js / 1024).toFixed(0)} KB JS + ${(parsed.css / 1024).toFixed(0)} KB CSS`);
  await page.goto(`${BASE}/`);
  await settled();
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  await new Promise((r) => setTimeout(r, 1500));
  await measure('dashboard (first load)');
  const navs = await page.$$eval('.bcv-nav__item[data-nav]', (els) => els.map((e) => e.dataset.nav));
  for (const id of navs.filter((n) => n !== 'dashboard')) {
    await page.click(`.bcv-nav__item[data-nav="${id}"]`).catch(() => {});
    await settled();
    await new Promise((r) => setTimeout(r, 1200));
    await measure(id);
  }
  // a course, its files, its grades, its modules — the heavy screens
  for (const [name, path] of [['course home', '/courses/101'], ['course files', '/courses/101/files'], ['course grades', '/courses/101/grades'], ['course modules', '/courses/101/modules'], ['assignment', '/courses/101/assignments/1007']]) {
    await page.goto(`${BASE}${path}`);
    await settled();
    await new Promise((r) => setTimeout(r, 1200));
    await measure(name);
  }
  // back and forth: what one round of every screen leaves behind (retained DOM, listeners, caches)
  await page.goto(`${BASE}/`);
  await settled();
  await new Promise((r) => setTimeout(r, 1200));
  await measure('dashboard (again)');
  for (let round = 0; round < 2; round++) for (const id of navs) { await page.click(`.bcv-nav__item[data-nav="${id}"]`).catch(() => {}); await settled(); await new Promise((r) => setTimeout(r, 400)); }
  await page.click('.bcv-nav__item[data-nav="dashboard"]').catch(() => {});
  await settled();
  await new Promise((r) => setTimeout(r, 1200));
  await measure('dashboard (after 2 rounds)');
  // idle: the heap and the requests while nothing happens
  console.log(`  idle on the Dashboard for ${IDLE_S}s…`);
  reqs = 0;
  const t0 = Date.now();
  for (let i = 0; i < IDLE_S / 5; i++) {
    await new Promise((r) => setTimeout(r, 5000));
    const m = await metrics();
    result.idle.push({ s: Math.round((Date.now() - t0) / 1000), heapMB: m.heapMB, nodes: m.nodes, listeners: m.listeners, reqs, layouts: m.layouts, restyles: m.restyles, taskS: m.taskS });
  }
  const first = result.idle[0], last = result.idle[result.idle.length - 1];
  console.log(`  idle: heap ${first.heapMB} → ${last.heapMB} MB, nodes ${first.nodes} → ${last.nodes}, listeners ${first.listeners} → ${last.listeners}, api requests ${reqs}, layouts +${last.layouts - first.layouts}, restyles +${last.restyles - first.restyles}, task time +${(last.taskS - first.taskS).toFixed(2)}s`);
  result.idle.reqs = reqs;
  result.storage = await sw.evaluate(async () => { const all = await self.BCV.api.storage.local.get(null); const sizes = Object.entries(all).map(([k, v]) => [k, JSON.stringify(v).length]).sort((a, b) => b[1] - a[1]); return { totalKB: +(sizes.reduce((s, [, n]) => s + n, 0) / 1024).toFixed(1), keys: sizes.length, top: sizes.slice(0, 8).map(([k, n]) => `${k}=${(n / 1024).toFixed(1)}KB`) }; });
  console.log(`  storage: ${result.storage.totalKB} KB in ${result.storage.keys} keys — ${result.storage.top.join(', ')}`);
  if (result.errors.length) console.log(`  page errors: ${result.errors.join(' | ')}`);
  writeFileSync(join(out, `measure-${label}.json`), JSON.stringify(result, null, 2));
  console.log(`  written scripts/dev/out/measure-${label}.json`);
} finally {
  await context.close().catch(() => {});
  server.kill();
  rmSync(extDir, { recursive: true, force: true });
  rmSync(userDataDir, { recursive: true, force: true });
}

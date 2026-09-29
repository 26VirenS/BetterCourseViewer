#!/usr/bin/env node
// What the extension costs in memory, as the operating system counts it: the private memory
// (Private_Clean + Private_Dirty, from /proc/<pid>/smaps_rollup) of the Canvas tab's renderer process with the extension and
// without it, the extension's own process (its service worker), and — inside the tab — the JS heap
// and the DOM. Each figure is taken after a forced garbage collection and a memory-pressure signal
// (Blink drops its caches), three times, the median kept. `node scripts/dev/ram.mjs before`, later
// `… after`; the numbers land in scripts/dev/out/ram-<label>.json. Linux only (it reads /proc).
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { mkdirSync, cpSync, readFileSync, writeFileSync, rmSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { afterMigration } from './harness.mjs';

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const prefix = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(prefix, 'x.js'))('playwright')); }

const label = process.argv[2] || 'now';
const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const out = join(root, 'scripts', 'dev', 'out');
mkdirSync(out, { recursive: true });
const PORT = 8807, SIM_PORT = 8808;
const BASE = `http://localhost:${PORT}`;
const MB = (kb) => +(kb / 1024).toFixed(1);

/** Every process of the browser run from this profile: pid, its type, whether it is the extension's, and its private memory in KB. */
function processes(profile) {
  const list = [];
  for (const d of readdirSync('/proc')) {
    if (!/^\d+$/.test(d)) continue;
    let cmd = '';
    try { cmd = readFileSync(`/proc/${d}/cmdline`, 'utf8').replace(/\0/g, ' '); } catch { continue; }
    if (!cmd.includes(profile)) continue;
    let mem = 0;
    try { const sm = readFileSync(`/proc/${d}/smaps_rollup`, 'utf8'); mem = Number(/^Private_Clean:\s+(\d+)/m.exec(sm)?.[1] || 0) + Number(/^Private_Dirty:\s+(\d+)/m.exec(sm)?.[1] || 0); } catch { continue; } // (private memory: what the process holds alone; PSS would split shared libraries across however many processes run)
    const type = /--type=(\S+)/.exec(cmd)?.[1] || 'browser';
    list.push({ pid: Number(d), type, ext: /--extension-process/.test(cmd), mem });
  }
  return list;
}
const median = (a) => { const s = [...a].sort((x, y) => x - y); return s[Math.floor(s.length / 2)]; };

async function run({ withExt }) {
  const profile = join(tmpdir(), `bcv-ram-${withExt ? 'ext' : 'none'}-${process.pid}`);
  const extDir = join(tmpdir(), `bcv-ram-extdir-${process.pid}`);
  let manifest = null;
  if (withExt) {
    cpSync(join(root, 'extension'), extDir, { recursive: true });
    manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
    for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*')) cs.matches.push(`${BASE}/*`);
    manifest.host_permissions.push(`${BASE}/*`);
    writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
  }
  const args = ['--js-flags=--expose-gc', '--disable-features=SpareRendererForSitePerProcess', '--renderer-process-limit=8'];
  if (withExt) args.push(`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`);
  const context = await chromium.launchPersistentContext(profile, { channel: 'chromium', headless: true, viewport: { width: 1400, height: 900 }, args });
  const res = { withExt, samples: [] };
  try {
    let sw = null;
    if (withExt) {
      [sw] = context.serviceWorkers();
      if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
      await afterMigration(sw);
      await new Promise((r) => setTimeout(r, 800));
      await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'welcome:report1': true, 'tools:welcomed': true, 'welcome:grades': true, 'setup:flow': 3, 'whatsnew:seen': v }), manifest.version);
    }
    for (const p of context.pages()) await p.close().catch(() => {});
    const page = await context.newPage();
    const cdp = await context.newCDPSession(page);
    const settled = () => page.waitForFunction(() => { const c = document.documentElement.classList; return !c.contains('bcv-on') || c.contains('bcv-settled'); }, null, { timeout: 20000 }).catch(() => {});
    const sample = async (name) => {
      const tabs = [], exts = [], gpus = [], heaps = [], nodes = [];
      for (let i = 0; i < 3; i++) {
        await cdp.send('HeapProfiler.collectGarbage').catch(() => {});
        await cdp.send('Memory.simulatePressureNotification', { level: 'critical' }).catch(() => {});
        await new Promise((r) => setTimeout(r, 900));
        const ps = processes(profile);
        const renderers = ps.filter((p) => p.type === 'renderer');
        tabs.push(renderers.filter((p) => !p.ext).reduce((s, p) => s + p.mem, 0));
        exts.push(renderers.filter((p) => p.ext).reduce((s, p) => s + p.mem, 0));
        gpus.push(ps.filter((p) => p.type === 'gpu-process').reduce((s, p) => s + p.mem, 0)); // (the page's layers are drawn and kept here)
        const h = await cdp.send('Runtime.getHeapUsage').catch(() => null);
        heaps.push(h ? h.usedSize / 1024 : 0);
        const d = await cdp.send('Memory.getDOMCounters').catch(() => null);
        nodes.push(d ? d.nodes : 0);
      }
      const s = { name, tabMB: MB(median(tabs)), extMB: MB(median(exts)), gpuMB: MB(median(gpus)), heapMB: MB(median(heaps)), nodes: median(nodes) };
      res.samples.push(s);
      console.log(`  ${withExt ? 'ext ' : 'none'}  ${name.padEnd(26)} tab ${String(s.tabMB).padStart(6)} MB   GPU ${String(s.gpuMB).padStart(6)} MB   extension process ${String(s.extMB).padStart(6)} MB   JS heap ${String(s.heapMB).padStart(5)} MB   DOM nodes ${s.nodes}`);
    };
    await page.goto(`${BASE}/`);
    await settled();
    if (withExt) await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 15000 }).catch(() => {});
    await new Promise((r) => setTimeout(r, 2500));
    await sample('dashboard');
    for (const [name, path] of [['grades', '/grades'], ['calendar', '/calendar'], ['course grades', '/courses/101/grades'], ['course modules', '/courses/101/modules'], ['assignment', '/courses/101/assignments/1007']]) {
      await page.goto(`${BASE}${path}`);
      await settled();
      await new Promise((r) => setTimeout(r, 2000));
      await sample(name);
    }
    // in place, without page loads: every screen of the sidebar twice over, then back to the Dashboard
    if (withExt) {
      await page.goto(`${BASE}/`);
      await settled();
      await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 15000 }).catch(() => {});
      const navs = await page.$$eval('.bcv-nav__item[data-nav]', (els) => els.map((e) => e.dataset.nav)).catch(() => []);
      for (let round = 0; round < 2; round++) for (const id of navs) { await page.click(`.bcv-nav__item[data-nav="${id}"]`, { timeout: 4000 }).catch(() => {}); await settled(); await new Promise((r) => setTimeout(r, 500)); }
      await page.click('.bcv-nav__item[data-nav="dashboard"]', { timeout: 4000 }).catch(() => {});
      await settled();
      await new Promise((r) => setTimeout(r, 2000));
      await sample('dashboard after 2 rounds');
    }
  } finally {
    await context.close().catch(() => {});
    rmSync(profile, { recursive: true, force: true });
    if (withExt) rmSync(extDir, { recursive: true, force: true });
  }
  return res;
}

const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT), String(SIM_PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));
try {
  console.log(`ram: ${label}`);
  const none = await run({ withExt: false });
  const ext = await run({ withExt: true });
  const extra = ext.samples.map((s) => { const b = none.samples.find((x) => x.name === s.name) || none.samples[0]; return { name: s.name, totalMB: +(s.tabMB + s.gpuMB + s.extMB).toFixed(1), tabMB: s.tabMB, gpuMB: s.gpuMB, extProcMB: s.extMB, heapMB: s.heapMB, nodes: s.nodes, bareTabMB: b.tabMB, bareGpuMB: b.gpuMB }; });
  console.log('  with the extension: the Canvas tab + the GPU process + the extension\'s own process (a plain tab of the same page, for scale):');
  for (const e of extra) console.log(`    ${e.name.padEnd(26)} ${String(e.totalMB).padStart(6)} MB = tab ${e.tabMB} + GPU ${e.gpuMB} + extension ${e.extProcMB}   (plain: tab ${e.bareTabMB} + GPU ${e.bareGpuMB})`);
  writeFileSync(join(out, `ram-${label}.json`), JSON.stringify({ label, at: new Date().toISOString(), none, ext, extra }, null, 2));
  console.log(`  written scripts/dev/out/ram-${label}.json`);
} finally {
  server.kill();
}

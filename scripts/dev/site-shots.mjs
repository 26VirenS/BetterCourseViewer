#!/usr/bin/env node
// The website's pictures of the interface (site/public/img/): the extension in Chromium at 2×, on the mock Canvas and
// the mock Brightspace, signed in as their made-up students at a made-up school ("Lakeside University", a plain crest
// for its logo), in light and in dark. Each picture is written as a 2× WebP and a 1× WebP for the page's srcset.
//   node scripts/dev/site-shots.mjs [out-dir]        (default: site/public/img)
// Requires Playwright (project or global install) and Python's Pillow (WebP).
import { createRequire } from 'node:module';
import { spawn, execSync, execFileSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync, rmSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { launchExtension, afterMigration } from './harness.mjs';

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const outDir = process.argv[2] || join(root, 'site', 'public', 'img');
const raw = join(tmpdir(), `bcv-site-shots-${process.pid}`);
mkdirSync(outDir, { recursive: true });
mkdirSync(raw, { recursive: true });
const PORT = 8940, SIM = 8941, D2L = 8942;
const BASE = `http://localhost:${PORT}`, D2L_BASE = `http://localhost:${D2L}`;

// the school's crest: a shield with an open book, in Lakeside's navy and gold (made up, like the school)
const crest = `data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96"><rect width="96" height="96" rx="22" fill="#12355b"/><path d="M48 16l24 9v20c0 16-10 27-24 33-14-6-24-17-24-33V25z" fill="#f2b632"/><path d="M34 40c5-2 10-2 14 1 4-3 9-3 14-1v20c-5-2-10-2-14 1-4-3-9-3-14-1z" fill="#12355b"/><path d="M48 41v20" stroke="#f2b632" stroke-width="2"/></svg>`)}`;

const extDir = join(tmpdir(), `bcv-site-ext-${process.pid}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*')) cs.matches.push(`${BASE}/*`, `${D2L_BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`, `${D2L_BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));

const canvas = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT), String(SIM)], { stdio: 'ignore' });
const d2l = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-brightspace.mjs'), String(D2L), '--open'], { stdio: 'ignore' });
process.on('exit', () => { canvas.kill(); d2l.kill(); });
for (const u of [`${BASE}/`, `${D2L_BASE}/d2l/login`]) for (let i = 0; i < 60; i++) { try { await fetch(u); break; } catch { await new Promise((r) => setTimeout(r, 100)); } }
await fetch(`${BASE}/__mock/config`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ richQuestions: true }) }).catch(() => {});

const userDataDir = join(tmpdir(), `bcv-site-profile-${process.pid}`);
const { context, sw } = await launchExtension(chromium, userDataDir, extDir, { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 });
const made = [];
try {
  await afterMigration(sw);
  for (const p of context.pages()) if (p.url().includes('/setup/')) await p.close().catch(() => {});
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'welcome:grades': true, 'tips:rubricRing': true, 'tools:welcomed': true, 'whatsnew:seen': v }), manifest.version);
  // a term's grade history, as a student who turned tracking on in August would have it (the Grades page's trend)
  await sw.evaluate(async (k) => {
    const p = (await chrome.storage.local.get(k))[k] || {};
    p.gpaTracking = { since: '2026-08-24', priorGpa: 3.36, priorCourses: 8 };
    p.gpaGoal = 3.5;
    p.gpaSnapshots = [['2026-08-24', 3.18], ['2026-08-31', 3.27], ['2026-09-07', 3.24], ['2026-09-14', 3.33], ['2026-09-21', 3.31], ['2026-09-28', 3.38], ['2026-10-05', 3.42]].map(([date, gpa]) => ({ date, gpa }));
    await chrome.storage.local.set({ [k]: p });
  }, `prefs:localhost:${PORT}`);
  const settings = (patch) => sw.evaluate(async (p) => self.BCV.settings.update(p), patch);
  await settings({ appearance: { siteName: 'Lakeside University', logoUrl: crest, darkMode: 'off' } });
  const page = await context.newPage();
  const settle = async () => {
    await page.waitForFunction(() => { const c = document.documentElement.classList; return c.contains('bcv-settled'); }, null, { timeout: 20000 }).catch(() => {});
    await page.waitForFunction(() => !document.querySelector('[data-rolling]'), null, { timeout: 6000 }).catch(() => {});
    await page.waitForTimeout(900);
    await page.mouse.move(1430, 890);
    await page.waitForTimeout(300);
  };
  const shot = async (name) => { await settle(); const f = join(raw, `${name}.png`); await page.screenshot({ path: f }); made.push(name); console.log('  shot', name); };
  const look = async (dark) => { await settings({ appearance: { darkMode: dark ? 'on' : 'off' } }); await page.waitForTimeout(250); };

  for (const dark of [false, true]) {
    const sfx = dark ? '-dark' : '';
    await look(dark);
    await page.goto(`${BASE}/`);
    await page.waitForSelector('.bcv-stat', { timeout: 20000 });
    await shot(`web-dashboard${sfx}`);
    await page.goto(`${BASE}/grades`);
    await page.waitForSelector('.bcv-gpa__value', { timeout: 20000 });
    await shot(`web-grades${sfx}`);
    await page.goto(`${BASE}/courses/101`);
    await page.waitForSelector('.bcv-cmain, .bcv-course', { timeout: 20000 }).catch(() => {});
    await shot(`web-course${sfx}`);
    await page.goto(`${BASE}/calendar`);
    await page.waitForTimeout(600);
    await shot(`web-calendar${sfx}`);
    // a quiz, a question at a time: begun, the first answer picked
    await page.goto(`${BASE}/courses/101/quizzes/9011`);
    await page.waitForSelector('.bcv-detail__actions .bcv-btn--primary', { timeout: 20000 });
    await page.click('.bcv-detail__actions .bcv-btn--primary');
    await page.waitForSelector('.bcv-qz__begin', { timeout: 20000 });
    await page.click('.bcv-qz__begin');
    await page.waitForSelector('.bcv-qz__opt', { timeout: 20000 });
    await page.waitForTimeout(900);
    await page.click('.bcv-qz__opt >> nth=3');
    await page.waitForSelector('.bcv-qz__opt.is-selected', { timeout: 8000 }).catch(() => {});
    await shot(`web-quiz${sfx}`);
    // and Brightspace's: the same interface over Lakeside's Brightspace
    await page.goto(`${D2L_BASE}/d2l/home`);
    await page.waitForSelector('.bcv-stat', { timeout: 20000 }).catch(() => {});
    await shot(`web-brightspace${sfx}`);
  }
} catch (e) {
  console.error('crashed:', e?.stack || e);
  process.exitCode = 1;
} finally {
  await context.close().catch(() => {});
  rmSync(userDataDir, { recursive: true, force: true });
  rmSync(extDir, { recursive: true, force: true });
}

// 2× and 1× WebP of each, for srcset (Pillow)
if (made.length) {
  execFileSync('python3', ['-c', `
import sys
from PIL import Image
raw, out = sys.argv[1], sys.argv[2]
for name in sys.argv[3:]:
    im = Image.open(f"{raw}/{name}.png").convert("RGB")
    im.save(f"{out}/{name}@2x.webp", "WEBP", quality=82, method=6)
    im.resize((im.width // 2, im.height // 2), Image.LANCZOS).save(f"{out}/{name}.webp", "WEBP", quality=84, method=6)
    print("  webp", name, im.size)
`, raw, outDir, ...made], { stdio: 'inherit' });
}
console.log(`raw PNGs in ${raw}`);
process.exit(process.exitCode || 0);

#!/usr/bin/env node
// Renders the Chrome Web Store listing assets into docs/store/ against the mock Canvas:
// five 1280×800 screenshots, a 440×280 small promo tile and a 1400×560 marquee.
// Loads the *Chrome* build of the manifest (service worker only) so the run doubles as a
// check that it works in Chromium. Requires Playwright (project or global install).
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { mkdirSync, cpSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';

const require = createRequire(import.meta.url);
let chromium;
try {
  ({ chromium } = require('playwright'));
} catch {
  const prefix = execSync('npm root -g').toString().trim();
  ({ chromium } = createRequire(join(prefix, 'x.js'))('playwright'));
}

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const out = join(root, 'docs', 'store');
mkdirSync(out, { recursive: true });
const PORT = 8789;
const BASE = `http://localhost:${PORT}`;

// the Chrome build's manifest (what scripts/package.sh ships), plus localhost for the mock
const extDir = join(tmpdir(), `bcv-store-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
delete manifest.background.scripts;
delete manifest.background.persistent;
delete manifest.author;
for (const cs of manifest.content_scripts) cs.matches.push(`${BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));

const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));

const userDataDir = join(tmpdir(), `bcv-store-profile-${Date.now()}`);
const context = await chromium.launchPersistentContext(userDataDir, {
  channel: 'chromium',
  headless: true,
  viewport: { width: 1280, height: 800 },
  deviceScaleFactor: 1,
  args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`],
});
// let every entrance (screen rise, stagger, bar fill) finish before the shot
const settle = async (page, ms = 400) => {
  await page.waitForFunction(() => document.getAnimations().every((a) => a.playState === 'finished' || a.playState === 'idle'), null, { timeout: 8000 }).catch(() => {});
  await page.waitForTimeout(ms);
};
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  console.log('extension loaded as a service worker:', new URL(sw.url()).host);
  const page = await context.newPage();
  page.on('pageerror', (e) => console.log('  page error:', e.message));
  const shot = (name) => page.screenshot({ path: join(out, `${name}.png`) });

  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 15000 });
  await page.waitForSelector('#bcv-progress[hidden]', { state: 'attached', timeout: 10000 });
  await settle(page);
  await shot('01-dashboard');

  await page.goto(`${BASE}/courses/101`);
  await page.waitForSelector('.bcv-front', { timeout: 15000 });
  await settle(page);
  await shot('02-course');

  await page.goto(`${BASE}/courses/101/grades`);
  await page.waitForSelector('.bcv-rings__svg', { timeout: 15000 });
  await settle(page);
  await shot('03-grades');

  await page.goto(`${BASE}/courses/101/quizzes/9001?bcv=feedback&sub=qs1`);
  await page.waitForSelector('.bcv-fb__q', { timeout: 15000 });
  await settle(page, 1800);
  await shot('04-quiz-feedback');

  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 15000 });
  await page.click('#bcv-theme-btn');
  await page.waitForFunction(() => document.documentElement.getAttribute('data-bcv-theme') === 'dark', null, { timeout: 5000 });
  await page.waitForTimeout(300); // the theme switch redraws the screen; wait for that entrance too
  await settle(page, 600);
  await shot('05-dark');
  await page.click('#bcv-theme-btn'); // leave the profile light again

  // promo tiles: the icon and a screenshot inset, drawn by the browser
  const icon = `data:image/png;base64,${readFileSync(join(root, 'extension', 'icons', 'icon-256.png')).toString('base64')}`;
  const dash = `data:image/png;base64,${readFileSync(join(out, '01-dashboard.png')).toString('base64')}`;
  const base = (w, h) => `html,body{margin:0;width:${w}px;height:${h}px;overflow:hidden;font-family:-apple-system,BlinkMacSystemFont,"Segoe UI",Helvetica,Arial,sans-serif;color:#fff}
    .bg{position:absolute;inset:0;background:linear-gradient(158deg,#0A84FF 0%,#0A4FD6 100%)}
    .shot{position:absolute;border-radius:10px;box-shadow:0 20px 60px rgba(0,0,0,.45);border:1px solid rgba(255,255,255,.18)}
    .icon{border-radius:23.5%;box-shadow:0 8px 24px rgba(0,0,0,.35)}`;
  // small tile: name row on top, the dashboard rising from the bottom edge
  const tileSmall = () => `<!doctype html><html><head><meta charset="utf-8"><style>${base(440, 280)}
    .row{position:absolute;left:24px;top:24px;right:24px;display:flex;align-items:center;gap:12px}
    .icon{width:40px;height:40px}
    .name{font-weight:800;font-size:23px;letter-spacing:-.03em;line-height:1}
    .sub{position:absolute;left:24px;top:78px;right:24px;font-weight:500;font-size:13px;line-height:1.35;opacity:.92}
    .shot{left:24px;top:118px;width:620px}
  </style></head><body><div class="bg"></div>
  <div class="row"><img class="icon" src="${icon}"><div class="name">Simpl Courses</div></div>
  <div class="sub">Canvas, quietly rebuilt — dashboard, courses, grades, quizzes and a smart panel.</div>
  <img class="shot" src="${dash}"></body></html>`;
  const tileMarquee = () => `<!doctype html><html><head><meta charset="utf-8"><style>${base(1400, 560)}
    .txt{position:absolute;left:64px;top:128px;width:520px}
    .icon{width:84px;height:84px;border-radius:22px}
    .name{font-weight:800;font-size:46px;letter-spacing:-.03em;line-height:1.05;margin:18px 0 14px}
    .sub{font-weight:500;font-size:20px;line-height:1.35;opacity:.92}
    .shot{right:-40px;top:70px;width:820px;border-radius:16px}
  </style></head><body><div class="bg"></div>
  <div class="txt"><img class="icon" src="${icon}"><div class="name">Simpl Courses</div><div class="sub">Canvas, quietly rebuilt — dashboard, courses, grades, quizzes and a smart panel, drawn from your own Canvas data</div></div>
  <img class="shot" src="${dash}"></body></html>`;
  const promo = await context.newPage();
  await promo.setViewportSize({ width: 440, height: 280 });
  await promo.setContent(tileSmall());
  await promo.waitForTimeout(300);
  await promo.screenshot({ path: join(out, 'promo-small.png') });
  await promo.setViewportSize({ width: 1400, height: 560 });
  await promo.setContent(tileMarquee());
  await promo.waitForTimeout(300);
  await promo.screenshot({ path: join(out, 'promo-marquee.png') });
  console.log(`store assets written to ${out}`);
} finally {
  await context.close();
  server.kill();
  rmSync(extDir, { recursive: true, force: true });
  rmSync(userDataDir, { recursive: true, force: true });
}

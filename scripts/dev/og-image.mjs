#!/usr/bin/env node
// The website's share picture (site/public/img/og.png, 1200×630): the app's icon, "Learning made Simpl." with Simpl
// in the site's blue, and the dashboard's window beside it, drawn by Chromium from the site's own pictures.
//   node scripts/dev/og-image.mjs
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { readFileSync } from 'node:fs';

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
// (inline: a page set from a string may not load files from disk)
const img = (f) => `data:image/${f.endsWith('.png') ? 'png' : 'webp'};base64,${readFileSync(join(root, 'site', 'public', 'img', f)).toString('base64')}`;
const html = `<!doctype html><html><head><meta charset="utf-8"><style>
  * { box-sizing: border-box; margin: 0; }
  body { width: 1200px; height: 630px; overflow: hidden; background: #fff; font-family: -apple-system, BlinkMacSystemFont, "SF Pro Display", "Segoe UI", system-ui, "Helvetica Neue", Arial, sans-serif; color: #1d1d1f; position: relative; }
  .glow { position: absolute; right: -120px; bottom: -160px; width: 760px; height: 560px; border-radius: 50%; background: #0a84ff; opacity: .14; filter: blur(90px); }
  .text { position: absolute; left: 72px; top: 78px; width: 500px; }
  .icon { width: 64px; height: 64px; border-radius: 15px; box-shadow: 0 16px 30px -14px rgba(0, 80, 200, .55); }
  h1 { margin-top: 30px; font-size: 84px; line-height: .96; font-weight: 700; letter-spacing: -.045em; }
  h1 span { color: #0071e3; }
  p { margin-top: 22px; font-size: 25px; line-height: 1.3; font-weight: 500; letter-spacing: -.02em; color: #6e6e73; }
  .win { position: absolute; left: 600px; top: 96px; width: 760px; border-radius: 12px; overflow: hidden; background: #fff; box-shadow: 0 0 0 .5px rgba(0, 0, 0, .14), 0 40px 90px -36px rgba(0, 20, 60, .32); }
  .bar { height: 40px; background: #ececee; border-bottom: 1px solid rgba(0, 0, 0, .1); display: flex; align-items: center; gap: 8px; padding: 0 14px; }
  .bar i { width: 12px; height: 12px; border-radius: 50%; background: #ff5f57; }
  .bar i:nth-child(2) { background: #febc2e; } .bar i:nth-child(3) { background: #28c840; }
  .win img { display: block; width: 100%; }
</style></head><body>
  <div class="glow"></div>
  <div class="text">
    <img class="icon" src="${img('icon-512.png')}">
    <h1>Learning made <span>Simpl.</span></h1>
    <p>Canvas and Brightspace, made simple.</p>
  </div>
  <div class="win"><div class="bar"><i></i><i></i><i></i></div><img src="${img('web-dashboard@2x.webp')}"></div>
</body></html>`;

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 630 }, deviceScaleFactor: 1 });
await page.setContent(html, { waitUntil: 'load' });
await page.evaluate(() => Promise.all([...document.images].map((i) => i.decode().catch(() => {}))));
await page.screenshot({ path: join(root, 'site', 'public', 'img', 'og.png') });
await browser.close();
console.log('site/public/img/og.png');

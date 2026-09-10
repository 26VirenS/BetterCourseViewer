#!/usr/bin/env node
// Renders the extension icon (an inline SVG) to PNGs at every size the
// manifest needs. Requires the `playwright` package (Chromium) on PATH:
//   npx playwright ... or a global install. Run: node scripts/make-icons.mjs
import { createRequire } from 'node:module';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
// Resolve playwright from the project first, then from the global npm prefix.
const require = createRequire(import.meta.url);
let chromium;
try {
  ({ chromium } = require('playwright'));
} catch {
  const { execSync } = await import('node:child_process');
  const prefix = execSync('npm root -g').toString().trim();
  ({ chromium } = createRequire(join(prefix, 'x.js'))('playwright'));
}
const outDir = join(root, 'extension', 'icons');
mkdirSync(outDir, { recursive: true });

const svg = (size) => `
<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 512 512">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#4f46e5"/>
      <stop offset="1" stop-color="#9333ea"/>
    </linearGradient>
  </defs>
  <rect x="24" y="24" width="464" height="464" rx="112" fill="url(#g)"/>
  <!-- open book -->
  <path d="M120 168c44-14 84-14 128 8v196c-44-22-84-22-128-8z" fill="#fff" fill-opacity="0.95"/>
  <path d="M392 168c-44-14-84-14-128 8v196c44-22 84-22 128-8z" fill="#fff" fill-opacity="0.8"/>
  <path d="M256 176v196" stroke="#4f46e5" stroke-width="10" stroke-linecap="round"/>
  <!-- checkmark -->
  <path d="M312 92l16 34 34 16-34 16-16 34-16-34-34-16 34-16z" fill="#fde68a"/>
</svg>`;

const browser = await chromium.launch();
const page = await browser.newPage();
for (const size of [48, 96, 128, 256, 512]) {
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(`<html><body style="margin:0;background:transparent">${svg(size)}</body></html>`);
  const png = await page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
  writeFileSync(join(outDir, `icon-${size}.png`), png);
  console.log(`wrote icon-${size}.png`);
}
await browser.close();

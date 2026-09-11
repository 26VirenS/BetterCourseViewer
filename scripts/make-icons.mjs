#!/usr/bin/env node
// Renders the Simpl Courses icon set from the logo's construction notes
// (a 120-unit grid: an assignment sheet dissolving into three grade rings).
//
//   extension/icons/icon-{48,96,128,256,512}.png   the blue tile (app icon, store icon, Chrome toolbar)
//   extension/icons/toolbar-{16,19,32,38,48,64}.png the mark alone, black on transparent — Safari draws
//                                                   the toolbar button from the alpha channel as a template
//   docs/brand/simpl-courses.svg, mark.svg          the vector sources
//   docs/brand/icon-1024.png, one-colour-{dark,light}.png
//
// Requires the `playwright` package (Chromium): a project or global install. Run: node scripts/make-icons.mjs
import { createRequire } from 'node:module';
import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
let chromium;
try {
  ({ chromium } = require('playwright'));
} catch {
  const { execSync } = await import('node:child_process');
  const prefix = execSync('npm root -g').toString().trim();
  ({ chromium } = createRequire(join(prefix, 'x.js'))('playwright'));
}
const iconsDir = join(root, 'extension', 'icons');
const brandDir = join(root, 'docs', 'brand');
mkdirSync(iconsDir, { recursive: true });
mkdirSync(brandDir, { recursive: true });

// ---- the mark, on the 120-unit grid ---------------------------------------------------------
// Arcs concentric on x 69, radii 30 / 18 / 6 (12-unit step); straight runs start at x 28 / 35 / 42
// (7-unit step); stroke 8 (9 on small tiles), round caps and joins, so every gap is 4 units.
// Sheet 53 × 84, radius 14, one stroke width beyond the outer arc on the left, top and bottom; its
// right edge is the arc centre, where the white fade (90% → 0%) reaches zero.
const CY = 60;
const strokes = (weight, colour) => `<g fill="none" stroke="${colour}" stroke-width="${weight}" stroke-linecap="round" stroke-linejoin="round">
    <path d="M28 ${CY - 30}H69A30 30 0 0 1 69 ${CY + 30}H28"/>
    <path d="M35 ${CY - 18}H69A18 18 0 0 1 69 ${CY + 18}H35"/>
    <path d="M42 ${CY - 6}H69A6 6 0 0 1 69 ${CY + 6}H42"/>
  </g>`;
const sheet = (id) => `<rect x="16" y="18" width="53" height="84" rx="14" fill="url(#${id})"/>`;
const sheetFade = (id, colour) => `<linearGradient id="${id}" gradientUnits="userSpaceOnUse" x1="16" y1="0" x2="69" y2="0">
      <stop offset="0" stop-color="${colour}" stop-opacity="0.9"/><stop offset="1" stop-color="${colour}" stop-opacity="0"/>
    </linearGradient>`;
// CSS `158deg`: the gradient line runs top-left → bottom-right, sized so the corners hit 0% and 100%
const tileGradient = (id, from, to) => {
  const rad = (158 * Math.PI) / 180;
  const dx = Math.sin(rad), dy = -Math.cos(rad);
  const len = 120 * Math.abs(dx) + 120 * Math.abs(dy);
  const f = (v) => v.toFixed(2);
  return `<linearGradient id="${id}" gradientUnits="userSpaceOnUse" x1="${f(60 - (dx * len) / 2)}" y1="${f(60 - (dy * len) / 2)}" x2="${f(60 + (dx * len) / 2)}" y2="${f(60 + (dy * len) / 2)}">
      <stop offset="0" stop-color="${from}"/><stop offset="1" stop-color="${to}"/>
    </linearGradient>`;
};
const RX = (120 * 0.235).toFixed(1); // 23.5% corner radius
// The 120-unit grid is the mark's own artboard: on the tile the mark is drawn at 0.72 (measured on the
// 1024 artwork: the mark spans 52% of the tile's width, centred, with the stroke at 4.8% of the tile).
const MARK_ON_TILE = 'translate(60 60) scale(0.72) translate(-59.5 -60)';

/** The blue tile. `weight`: stroke width (8 large, 9 small). */
const tileSvg = (weight = 8, { size = 120, fill = 'gradient', ink = '#fff' } = {}) => `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 120 120">
  <defs>
    ${fill === 'gradient' ? tileGradient('tile', '#0A84FF', '#0A4FD6') : ''}
    ${sheetFade('sheet', ink)}
  </defs>
  <rect width="120" height="120" rx="${RX}" fill="${fill === 'gradient' ? 'url(#tile)' : fill}"/>
  <g transform="${MARK_ON_TILE}">
    ${sheet('sheet')}
    ${strokes(weight, ink)}
  </g>
</svg>`;

/** The mark alone on a transparent square (the toolbar template and the vector source). */
const markSvg = (weight = 8, { size = 100, ink = '#000' } = {}) => `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="10 10 100 100">
  <defs>${sheetFade('sheet', ink)}</defs>
  ${sheet('sheet')}
  ${strokes(weight, ink)}
</svg>`;

writeFileSync(join(brandDir, 'simpl-courses.svg'), tileSvg(8, { size: 1024 }) + '\n');
writeFileSync(join(brandDir, 'mark.svg'), markSvg(8, { size: 512 }) + '\n');

const browser = await chromium.launch();
const page = await browser.newPage({ deviceScaleFactor: 1 });
async function render(svg, size, file) {
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(`<html><body style="margin:0;background:transparent">${svg}</body></html>`);
  const png = await page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
  writeFileSync(file, png);
  console.log('wrote', file.replace(root + '/', ''));
}
for (const size of [48, 96, 128, 256, 512]) await render(tileSvg(size <= 64 ? 9 : 8, { size }), size, join(iconsDir, `icon-${size}.png`));
for (const size of [16, 19, 32, 38, 48, 64]) await render(markSvg(size <= 38 ? 9 : 8, { size }), size, join(iconsDir, `toolbar-${size}.png`));
await render(tileSvg(8, { size: 1024 }), 1024, join(brandDir, 'icon-1024.png'));
await render(tileSvg(8, { size: 512, fill: '#1c1c1e', ink: '#fff' }), 512, join(brandDir, 'one-colour-dark.png'));
await render(tileSvg(8, { size: 512, fill: '#e5e5ea', ink: '#000' }), 512, join(brandDir, 'one-colour-light.png'));
await browser.close();

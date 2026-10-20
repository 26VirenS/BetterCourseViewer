#!/usr/bin/env node
// Renders the Simpl icon set from the logo's construction (a 120-unit grid): a black rounded tile, a bold
// white S, and a blue dot at its foot on the right — "S.".
//
//   extension/icons/icon-{48,96,128,256,512}.png   the tile (app icon, store icon, Chrome toolbar)
//   extension/icons/toolbar-{16,19,32,38,48,64}.png the S and dot alone, black on transparent — Safari draws
//                                                   the toolbar button from the alpha channel as a template
//   mac/…/AppIcon.appiconset, macos/…/AppIcon.appiconset, macos/…/Resources/Icon.png
//                                                   the tile on the macOS icon grid (824 of 1024, a soft shadow)
//   ios/…/AppIcon.appiconset/icon-1024.png          an opaque square (iOS masks its own corners)
//   site/public/img/icon-{128,512}.png, apple-touch-icon.png
//   docs/brand/simpl-courses.svg, mark.svg          the vector sources
//   docs/brand/icon-1024.png, one-colour-{dark,light}.png
//   with --win <dir>: <dir>/Simpl-256.png and <dir>/Simpl.ico (16–256) for the Windows apps
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
const winAt = process.argv.indexOf('--win');
const winDir = winAt > 0 ? process.argv[winAt + 1] : null;
const iconsDir = join(root, 'extension', 'icons');
const brandDir = join(root, 'docs', 'brand');
mkdirSync(iconsDir, { recursive: true });
mkdirSync(brandDir, { recursive: true });

// ---- the mark, on the 120-unit grid ---------------------------------------------------------
// The S is one stroke (12.5 wide) through two stacked half-ellipses (rx 17, ry 13.5) meeting at y 58, its
// ends cut along the radius at -38° and 142°; the dot (r 7) sits at its foot on the right. The pair is
// moved 5 left and 1 down so that S and dot together are centred on the tile.
export const S_PATH = 'M73.40 36.19A17 13.5 0 1 0 60 58A17 13.5 0 1 1 46.60 80.81';
export const BLUE = '#0A84FF';
const mark = (ink, dot = BLUE) => `<g transform="translate(-5 1)"><path d="${S_PATH}" fill="none" stroke="${ink}" stroke-width="12.5"/><circle cx="87" cy="83" r="7" fill="${dot}"/></g>`;
const RX = (120 * 0.235).toFixed(1); // 23.5% corner radius

/** The tile. `plate`: 'full' (rounded, edge to edge), 'square' (iOS masks its own), 'mac' (the macOS grid). */
const tileSvg = ({ size = 120, fill = 'gradient', ink = '#fff', dot = BLUE, plate = 'full' } = {}) => {
  // the macOS grid: the tile 824 of 1024, centred, with room for its shadow
  const vb = plate === 'mac' ? `${(-60 * 1024 / 824 + 60).toFixed(2)} ${(-60 * 1024 / 824 + 60).toFixed(2)} ${(120 * 1024 / 824).toFixed(2)} ${(120 * 1024 / 824).toFixed(2)}` : '0 0 120 120';
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="${vb}">
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1c1c1e"/><stop offset="1" stop-color="#000"/></linearGradient>
    <filter id="lift" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="2.2"/></filter>
  </defs>
  ${plate === 'mac' ? `<rect y="1.6" width="120" height="120" rx="${RX}" fill="#000" opacity="0.32" filter="url(#lift)"/>` : ''}
  <rect width="120" height="120" rx="${plate === 'square' ? 0 : RX}" fill="${fill === 'gradient' ? 'url(#tile)' : fill}"/>
  ${mark(ink, dot)}
</svg>`;
};

/** The S and dot alone on a transparent square (the toolbar template and the vector source). */
const markSvg = ({ size = 100, ink = '#000', dot = ink } = {}) => `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="23 23 74 74">
  ${mark(ink, dot)}
</svg>`;

writeFileSync(join(brandDir, 'simpl-courses.svg'), tileSvg({ size: 1024 }) + '\n');
writeFileSync(join(brandDir, 'mark.svg'), markSvg({ size: 512, dot: BLUE }) + '\n');

const browser = await chromium.launch();
const page = await browser.newPage({ deviceScaleFactor: 1 });
async function png(svg, size) {
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(`<html><body style="margin:0;background:transparent">${svg}</body></html>`);
  return page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
}
async function render(svg, size, file) {
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, await png(svg, size));
  console.log('wrote', file.replace(root + '/', ''));
}
for (const size of [48, 96, 128, 256, 512]) await render(tileSvg({ size }), size, join(iconsDir, `icon-${size}.png`));
for (const size of [16, 19, 32, 38, 48, 64]) await render(markSvg({ size }), size, join(iconsDir, `toolbar-${size}.png`));
await render(tileSvg({ size: 1024 }), 1024, join(brandDir, 'icon-1024.png'));
await render(tileSvg({ size: 1024, plate: 'square' }), 1024, join(root, 'ios', 'SimplCourses', 'Resources', 'Assets.xcassets', 'AppIcon.appiconset', 'icon-1024.png'));
await render(tileSvg({ size: 512, fill: '#1c1c1e', dot: '#fff' }), 512, join(brandDir, 'one-colour-dark.png'));
await render(tileSvg({ size: 512, fill: '#e5e5ea', ink: '#000', dot: '#000' }), 512, join(brandDir, 'one-colour-light.png'));
// the Mac apps (the native app and the Safari extension's container app): the macOS grid at every size
for (const set of [join(root, 'mac', 'Simpl', 'Assets.xcassets', 'AppIcon.appiconset'), join(root, 'macos', 'Simpl Courses', 'Simpl Courses', 'Assets.xcassets', 'AppIcon.appiconset')]) {
  for (const pt of [16, 32, 128, 256, 512]) for (const k of [1, 2]) await render(tileSvg({ size: pt * k, plate: 'mac' }), pt * k, join(set, `mac-icon-${pt}@${k}x.png`));
}
await render(tileSvg({ size: 512, plate: 'mac' }), 512, join(root, 'macos', 'Simpl Courses', 'Simpl Courses', 'Resources', 'Icon.png'));
// the website
const site = join(root, 'site', 'public', 'img');
await render(tileSvg({ size: 128 }), 128, join(site, 'icon-128.png'));
await render(tileSvg({ size: 512 }), 512, join(site, 'icon-512.png'));
await render(tileSvg({ size: 180, plate: 'square' }), 180, join(site, 'apple-touch-icon.png'));
// the Windows apps: a PNG and an .ico of PNGs (16–256)
if (winDir) {
  mkdirSync(winDir, { recursive: true });
  await render(tileSvg({ size: 256 }), 256, join(winDir, 'Simpl-256.png'));
  const sizes = [16, 32, 64, 128, 256];
  const pngs = [];
  for (const s of sizes) pngs.push(await png(tileSvg({ size: s }), s));
  const head = Buffer.alloc(6 + 16 * sizes.length);
  head.writeUInt16LE(0, 0); head.writeUInt16LE(1, 2); head.writeUInt16LE(sizes.length, 4);
  let at = head.length;
  sizes.forEach((s, i) => {
    const o = 6 + 16 * i;
    head.writeUInt8(s === 256 ? 0 : s, o); head.writeUInt8(s === 256 ? 0 : s, o + 1);
    head.writeUInt16LE(1, o + 4); head.writeUInt16LE(32, o + 6);
    head.writeUInt32LE(pngs[i].length, o + 8); head.writeUInt32LE(at, o + 12);
    at += pngs[i].length;
  });
  writeFileSync(join(winDir, 'Simpl.ico'), Buffer.concat([head, ...pngs]));
  console.log('wrote', join(winDir, 'Simpl.ico'));
}
await browser.close();

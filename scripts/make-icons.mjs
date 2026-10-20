#!/usr/bin/env node
// Renders the Simpl icon set from the logo (docs/brand/logo-source.png), on a 120-unit grid: a black rounded tile, a
// bold white S, and a blue dot at its foot on the right — "S.".
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
// The S is the given logo's own (docs/brand/logo-source.png, 440 px): its outline traced at 6x (marching squares on
// the white, simplified to 0.25 px of the original), so every size is drawn from the artwork itself, not redrawn. The
// dot (#0A84FF) and the tile's corner are measured from the same picture; the tile is flat black, as there.
export const S_PATH = 'M50.55 24.94L50.27 25.03L46.73 25.11L46.23 25.29L44.82 25.39L44.32 25.57L43.28 25.64L42.95 25.84L42.19 25.91L41.86 26.11L41.05 26.20L40.77 26.38L40.45 26.39L39.36 26.76L39.09 26.94L38.55 27.03L38.32 27.20L37.18 27.57L34.61 28.91L32.18 30.57L30.07 32.50L28.15 34.82L28.00 35.21L27.57 35.68L27.18 36.57L26.77 37.01L26.56 37.50L26.58 37.73L26.01 38.86L26.03 39.09L25.68 39.82L25.08 42.55L25.03 44.27L24.84 45.64L25.03 46.77L25.11 48.68L25.29 49.18L25.39 50.32L25.56 50.55L25.65 51.09L25.84 51.36L26.19 52.59L27.03 54.27L28.41 56.34L30.02 58.05L31.55 59.36L32.41 59.93L32.76 60.05L34.05 61.02L36.68 62.30L36.91 62.29L38.86 63.12L39.09 63.11L40.50 63.67L44.64 64.63L44.86 64.76L51.91 66.11L55.45 66.92L56.05 67.21L56.27 67.20L56.86 67.49L57.09 67.47L57.68 67.76L57.91 67.74L59.05 68.30L59.27 68.29L59.55 68.55L59.82 68.56L60.19 68.91L61.05 69.30L62.32 70.50L62.92 71.27L63.47 72.36L63.56 72.91L63.75 73.18L63.79 76.00L63.38 77.32L63.39 77.55L63.13 78.09L62.16 79.55L61.30 80.41L59.86 81.47L58.23 82.29L56.86 82.65L56.59 82.84L55.85 82.91L55.05 83.15L53.82 83.21L53.32 83.39L49.23 83.39L46.50 83.03L43.92 82.32L43.18 81.92L42.91 81.92L42.64 81.65L42.36 81.64L42.09 81.38L41.82 81.37L40.67 80.50L40.28 80.36L38.82 78.91L38.23 78.17L37.12 75.95L37.02 75.09L36.84 74.86L36.83 74.55L36.68 74.40L23.77 74.39L23.55 74.44L23.47 74.64L23.47 75.36L23.66 75.91L23.75 77.32L23.92 77.77L24.01 78.64L24.21 78.95L24.29 79.73L24.50 80.09L24.57 80.86L25.56 82.73L25.67 83.32L26.10 83.82L26.47 84.55L27.82 86.39L29.86 88.50L30.59 88.95L31.45 89.82L32.49 90.32L33.68 91.22L34.23 91.49L34.45 91.47L34.77 91.76L35.00 91.74L35.41 91.91L36.77 92.62L39.95 93.67L44.09 94.49L44.55 94.47L47.05 94.84L49.59 94.89L54.68 94.84L55.45 94.66L58.64 94.32L59.39 94.09L60.14 94.02L60.36 93.85L61.18 93.76L61.45 93.57L62.23 93.50L63.41 93.01L63.95 92.93L64.36 92.69L64.82 92.64L68.59 90.74L69.91 89.76L70.23 89.65L72.15 88.00L73.77 86.27L74.23 85.55L74.91 84.85L75.59 83.38L76.02 82.91L77.30 79.45L77.85 76.73L77.83 75.73L77.98 74.50L77.93 71.27L77.75 70.77L77.66 69.64L77.49 69.18L77.38 68.27L77.20 68.00L76.84 66.64L75.73 64.42L74.64 62.80L73.03 61.09L72.46 60.73L71.77 60.05L71.10 59.64L70.67 59.23L69.68 58.77L69.27 58.39L67.05 57.28L66.64 57.10L66.41 57.12L65.27 56.56L65.05 56.58L64.41 56.26L62.82 55.74L62.59 55.76L59.82 54.92L57.59 54.51L57.36 54.38L48.95 52.76L45.56 51.77L44.14 51.20L42.50 50.38L42.04 49.95L41.65 49.82L40.18 48.36L39.85 47.86L39.30 46.77L39.02 44.59L39.12 44.23L39.11 43.50L39.40 42.64L39.38 42.41L40.41 40.44L41.47 39.27L42.09 38.76L42.41 38.65L42.91 38.21L44.50 37.41L44.95 37.35L45.36 37.12L46.23 37.02L46.45 36.85L47.59 36.75L48.14 36.56L51.00 36.47L53.82 36.66L55.87 37.14L56.77 37.44L57.95 38.03L58.23 38.04L58.56 38.36L59.41 38.76L60.43 39.59L61.27 40.44L62.08 41.50L62.65 42.64L62.76 43.23L62.93 43.45L63.20 44.50L63.27 45.32L63.41 45.49L76.45 45.52L76.65 45.41L76.57 43.77L76.39 43.23L76.30 41.59L76.10 41.27L75.52 38.95L75.30 38.59L74.92 37.45L74.36 36.33L73.00 34.16L71.64 32.53L69.32 30.33L68.92 30.18L68.45 29.75L68.10 29.64L67.67 29.23L67.28 29.09L66.85 28.68L64.64 27.57L61.18 26.29L58.36 25.63L55.41 25.19Z';
export const BLUE = '#0A84FF';
export const DOT = { cx: 89.73, cy: 87.0, r: 7.56 };
const mark = (ink, dot = BLUE) => `<path d="${S_PATH}" fill="${ink}"/><circle cx="${DOT.cx}" cy="${DOT.cy}" r="${DOT.r}" fill="${dot}"/>`;
const RX = '27.47'; // (the given tile's corner, measured along its diagonal)

/** The tile. `plate`: 'full' (rounded, edge to edge), 'square' (iOS masks its own), 'mac' (the macOS grid). */
const tileSvg = ({ size = 120, fill = 'gradient', ink = '#fff', dot = BLUE, plate = 'full' } = {}) => {
  // the macOS grid: the tile 824 of 1024, centred, with room for its shadow
  const vb = plate === 'mac' ? `${(-60 * 1024 / 824 + 60).toFixed(2)} ${(-60 * 1024 / 824 + 60).toFixed(2)} ${(120 * 1024 / 824).toFixed(2)} ${(120 * 1024 / 824).toFixed(2)}` : '0 0 120 120';
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="${vb}">
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#000"/><stop offset="1" stop-color="#000"/></linearGradient>
    <filter id="lift" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="2.2"/></filter>
  </defs>
  ${plate === 'mac' ? `<rect y="1.6" width="120" height="120" rx="${RX}" fill="#000" opacity="0.32" filter="url(#lift)"/>` : ''}
  <rect width="120" height="120" rx="${plate === 'square' ? 0 : RX}" fill="${fill === 'gradient' ? 'url(#tile)' : fill}"/>
  ${mark(ink, dot)}
</svg>`;
};

/** The S and dot alone on a transparent square (the toolbar template and the vector source). */
const markSvg = ({ size = 100, ink = '#000', dot = ink } = {}) => `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="22.9 22.4 75 75">
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

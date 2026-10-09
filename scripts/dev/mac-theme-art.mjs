#!/usr/bin/env node
// Simpl for Mac's theme backdrops (1.3): each of the web's six drawn scenes, drawn to the window's shape
// (extension/lib/theme.js backScene) and inked as the web inks them (inkFor: the outlines and the darkest masses as a
// mask, white where the ink goes), written into the app's asset catalogue as template images (Theme<Name>.imageset),
// which the app colours in the theme's ink. Run again when a scene's drawing changes:
//   node scripts/dev/mac-theme-art.mjs [--preview <dir>]   (--preview: each also coloured as the app shows it, to look at)
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const ASSETS = join(ROOT, 'mac', 'Simpl', 'Assets.xcassets');
const SCENES = ['Dusk', 'Ocean', 'Forest', 'Sand', 'Peaks', 'City'];
// the ready-made themes' accents (content/app/personalize.js READY): only for the previews
const ACCENT = { Dusk: '#ff375f', Ocean: '#40c8e0', Forest: '#30d158', Sand: '#ff9f0a', Peaks: '#5e5ce6', City: '#bf5af2' };
const previewAt = process.argv.includes('--preview') ? process.argv[process.argv.indexOf('--preview') + 1] : null;

const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM || undefined });
const page = await browser.newPage();
await page.setContent('<!doctype html><title>art</title>');
await page.addScriptTag({ content: readFileSync(join(ROOT, 'extension', 'lib', 'theme.js'), 'utf8') });
for (const name of SCENES) {
  const out = await page.evaluate(async ({ name, accent, preview }) => {
    const T = self.BCV.theme;
    const url = T.backScene(name);
    const { ink } = await T.inkFor(url, { wide: 2400 });
    if (!preview) return { ink };
    // as the app shows it, by day and by night: the paper (the page cast with the accent), the ink on it
    const img = await new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = ink; });
    const shots = [];
    for (const [paper0, cast, k] of [['#f5f5f7', 0.12, 0.3], ['#161618', 0.1, 0.34]]) {
      const mix = (a, b, t) => { const p = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16)); const A = p(a), B = p(b); return `rgb(${A.map((v, i) => Math.round(v + (B[i] - v) * t)).join(',')})`; };
      const hex = (rgb) => '#' + rgb.match(/\d+/g).map((v) => Number(v).toString(16).padStart(2, '0')).join('');
      const paper = hex(mix(paper0, accent, cast));
      const cv = document.createElement('canvas'); cv.width = img.width; cv.height = img.height;
      const cx = cv.getContext('2d');
      const ic = document.createElement('canvas'); ic.width = img.width; ic.height = img.height;
      const ix = ic.getContext('2d'); ix.drawImage(img, 0, 0); ix.globalCompositeOperation = 'source-in'; ix.fillStyle = mix(paper, accent, k); ix.fillRect(0, 0, ic.width, ic.height);
      cx.fillStyle = paper; cx.fillRect(0, 0, cv.width, cv.height); cx.drawImage(ic, 0, 0);
      shots.push(cv.toDataURL('image/png'));
    }
    return { ink, shots };
  }, { name, accent: ACCENT[name], preview: !!previewAt });
  const set = join(ASSETS, `Theme${name}.imageset`);
  mkdirSync(set, { recursive: true });
  writeFileSync(join(set, `Theme${name}.png`), Buffer.from(out.ink.split(',')[1], 'base64'));
  writeFileSync(join(set, 'Contents.json'), JSON.stringify({
    images: [{ filename: `Theme${name}.png`, idiom: 'universal' }],
    info: { author: 'xcode', version: 1 },
    properties: { 'template-rendering-intent': 'template' },
  }, null, 2) + '\n');
  if (previewAt) {
    mkdirSync(previewAt, { recursive: true });
    out.shots.forEach((d, i) => writeFileSync(join(previewAt, `${name}-${i ? 'dark' : 'light'}.png`), Buffer.from(d.split(',')[1], 'base64')));
  }
  console.log(`Theme${name}: ${Math.round(Buffer.byteLength(out.ink) * 0.75 / 1024)} KB`);
}
await browser.close();

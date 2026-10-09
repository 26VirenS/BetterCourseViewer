#!/usr/bin/env node
// Simpl for Mac's ready-made photos (1.3.1): each of the web's six drawn scenes as the web draws them for the sidebar
// (its tall drawing) and for the Dashboard's counters (its wide one, a variation for each of the six), inked as the web
// inks them (inkFor: the outlines and the darkest masses as a mask, white where the ink goes), written into the app's
// asset catalogue as template images (Theme<Name>Side, Theme<Name>Card0…5), which the app colours in the theme's ink.
// Run again when a scene's drawing changes:
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
const PARTS = [['Side', 'side', 0], ...[0, 1, 2, 3, 4, 5].map((k) => [`Card${k}`, 'card', k])];
for (const name of SCENES) {
  for (const [suffix, place, k] of PARTS) {
    const out = await page.evaluate(async ({ name, place, k, accent, preview }) => {
      const T = self.BCV.theme;
      const url = T.sceneUrl(name, k, place);
      const { ink } = await T.inkFor(url, { wide: 1000 });
      if (!preview) return { ink };
      const img = await new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = ink; });
      const mix = (a, b, t) => { const p = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16)); const A = p(a), B = p(b); return `rgb(${A.map((v, i) => Math.round(v + (B[i] - v) * t)).join(',')})`; };
      const hex = (rgb) => '#' + rgb.match(/\d+/g).map((v) => Number(v).toString(16).padStart(2, '0')).join('');
      const paper = hex(mix('#f5f5f7', accent, 0.12));
      const cv = document.createElement('canvas'); cv.width = img.width; cv.height = img.height;
      const cx = cv.getContext('2d');
      const ic = document.createElement('canvas'); ic.width = img.width; ic.height = img.height;
      const ix = ic.getContext('2d'); ix.drawImage(img, 0, 0); ix.globalCompositeOperation = 'source-in'; ix.fillStyle = mix(paper, accent, 0.3); ix.fillRect(0, 0, ic.width, ic.height);
      cx.fillStyle = paper; cx.fillRect(0, 0, cv.width, cv.height); cx.drawImage(ic, 0, 0);
      return { ink, shot: cv.toDataURL('image/png') };
    }, { name, place, k, accent: ACCENT[name], preview: !!previewAt });
    const id = `Theme${name}${suffix}`;
    const set = join(ASSETS, `${id}.imageset`);
    mkdirSync(set, { recursive: true });
    writeFileSync(join(set, `${id}.png`), Buffer.from(out.ink.split(',')[1], 'base64'));
    writeFileSync(join(set, 'Contents.json'), JSON.stringify({
      images: [{ filename: `${id}.png`, idiom: 'universal' }],
      info: { author: 'xcode', version: 1 },
      properties: { 'template-rendering-intent': 'template' },
    }, null, 2) + '\n');
    if (previewAt) { mkdirSync(previewAt, { recursive: true }); writeFileSync(join(previewAt, `${id}.png`), Buffer.from(out.shot.split(',')[1], 'base64')); }
    console.log(`${id}: ${Math.round(Buffer.byteLength(out.ink) * 0.75 / 1024)} KB`);
  }
}
await browser.close();

// Browser zoom and OS display scaling. A zoom (or a scaled display) divides the window into fewer,
// larger CSS pixels and raises devicePixelRatio, so a laptop at 200% is a phone-wide viewport and
// one at 150% is a compact one. Four such windows, a browser context each (the metrics are the
// context's), the main screens and the overlays at each: nothing scrolls sideways, nothing runs
// past the viewport, no fixed element is off screen, no text is cut without an ellipsis, no two
// siblings land on each other, no card is squeezed under 200px; the tiers land where the CSS says
// (phone ≤ 700, compact ≤ 1100), popups stay inside the window, and a zoom that crosses the phone
// line while the interface is up reloads the page into the other layout.
// Run: node scripts/dev/zoom-test.mjs
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync, rmSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { afterMigration } from './harness.mjs';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const OUT = join(root, 'scripts/dev/out');
mkdirSync(OUT, { recursive: true });
const PORT = 8811; // (its own: the suites run side by side under test-all.mjs)
const BASE = `http://localhost:${PORT}`;
const extDir = join(tmpdir(), `bcv-zoom-ext-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*')) cs.matches.push(`${BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };

// the windows: a content area in device pixels (after the browser's own chrome) and a zoom
const COMBOS = [
  { label: 'laptop-200', w: 1366, h: 620, z: 2 },     // 683×310: a phone-wide viewport with a mouse
  { label: 'laptop-150', w: 1366, h: 620, z: 1.5 },   // 911×413: compact
  { label: '1080p-175', w: 1920, h: 930, z: 1.75 },   // 1097×531: compact, short
  { label: 'laptop-67', w: 1366, h: 620, z: 0.67 },   // 2039×925: zoomed out
].map((c) => ({ ...c, cw: Math.round(c.w / c.z), ch: Math.round(c.h / c.z) }));
const SCREENS = [['dashboard', '/'], ['courses', '/courses'], ['calendar', '/calendar'], ['course-home', '/courses/101'], ['assignment', '/courses/104/assignments/4002'], ['tools', '/#tools'], ['punch', '/courses/101/external_tools/9']];

/** In the page: what breaks at this size (see the file's header). */
const MEASURE = () => {
  const vw = innerWidth, vh = innerHeight;
  const html = document.documentElement;
  const res = { vw, vh, dpr: devicePixelRatio, phone: html.classList.contains('bcv-phone'), sideways: html.scrollWidth > vw + 1 || (document.getElementById('bcv-app')?.scrollWidth || 0) > vw + 1, wide: [], fixedOff: [], cut: [], overlap: [], narrow: [] };
  const name = (el) => `${el.tagName.toLowerCase()}${el.id ? '#' + el.id : ''}.${String(el.className && el.className.baseVal !== undefined ? el.className.baseVal : el.className || '').split(' ').filter(Boolean).slice(0, 2).join('.')}`;
  const visible = (el, cs) => cs.display !== 'none' && cs.visibility !== 'hidden' && cs.opacity !== '0';
  const inScroller = (el) => { let p = el.parentElement, n = 0; while (p && p !== document.body && n++ < 12) { const c = getComputedStyle(p); if (/(auto|scroll)/.test(c.overflowX) || /(auto|scroll)/.test(c.overflow)) return true; p = p.parentElement; } return false; };
  const roots = [...document.querySelectorAll('#bcv-app, .bcv-sheet-ov, #bcv-tray, #bcv-bar, .bcv-omni__panel, .bcv-menu, .bcv-quicknav, .bcv-toast')];
  const wn = document.querySelector('#bcv-whatsnew');
  if (wn?.shadowRoot) roots.push(wn.shadowRoot);
  const seenWide = new Set();
  let count = 0;
  for (const rootEl of roots) {
    for (const el of rootEl.querySelectorAll('*')) {
      if (++count > 6000) break;
      if (el.closest('svg') && el.tagName.toLowerCase() !== 'svg') continue;
      const cs = getComputedStyle(el);
      if (!visible(el, cs)) continue;
      const r = el.getBoundingClientRect();
      if (!r.width || !r.height) continue;
      if ((r.right > vw + 1 || r.left < -1) && !seenWide.has(el.parentElement) && !inScroller(el) && cs.position !== 'fixed') { seenWide.add(el.parentElement); if (res.wide.length < 4) res.wide.push(`${name(el)} l=${Math.round(r.left)} r=${Math.round(r.right)}`); }
      if (cs.position === 'fixed' && (r.right > vw + 1 || r.bottom > vh + 1 || r.left < -1 || r.top < -1) && res.fixedOff.length < 4) res.fixedOff.push(`${name(el)} ${Math.round(r.left)},${Math.round(r.top)} ${Math.round(r.width)}×${Math.round(r.height)}`);
      if (cs.overflow === 'hidden' || cs.overflowX === 'hidden') {
        const text = el.childElementCount === 0 ? el.textContent.trim() : '';
        if (text && cs.whiteSpace === 'nowrap' && cs.textOverflow !== 'ellipsis' && el.scrollWidth > el.clientWidth + 2 && res.cut.length < 4) res.cut.push(`${name(el)} "${text.slice(0, 28)}"`);
        else if (text && cs.whiteSpace !== 'nowrap' && el.scrollHeight > el.clientHeight + 4 && res.cut.length < 4) res.cut.push(`${name(el)} tall "${text.slice(0, 28)}"`);
      }
      if (el.childElementCount >= 2 && el.childElementCount <= 40 && cs.display !== 'grid' && res.overlap.length < 4) {
        // (inline siblings wrap across lines and their boxes cross by nature: only blocks and inline-blocks count)
        const kids = [...el.children].filter((k) => { const c = getComputedStyle(k); return (c.position === 'static' || c.position === 'relative') && c.display !== 'inline' && visible(k, c) && k.textContent.trim(); }).map((k) => ({ k, r: k.getBoundingClientRect() })).filter((x) => x.r.width && x.r.height);
        for (let i = 0; i < kids.length; i++) for (let j = i + 1; j < kids.length; j++) {
          const a = kids[i].r, b = kids[j].r;
          const ox = Math.min(a.right, b.right) - Math.max(a.left, b.left), oy = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
          if (ox > 6 && oy > 6 && res.overlap.length < 4) res.overlap.push(`${name(kids[i].k)} × ${name(kids[j].k)} ${Math.round(ox)}×${Math.round(oy)}`);
        }
      }
    }
  }
  for (const c of document.querySelectorAll('.bcv-ccard, .bcv-tool-card, .bcv-stat')) { const r = c.getBoundingClientRect(); if (r.width && r.width < 200 && res.narrow.length < 3) res.narrow.push(`${c.className.split(' ')[0]} ${Math.round(r.width)}px`); }
  res.sideW = getComputedStyle(html).getPropertyValue('--bcv-side-w').trim();
  const stats = document.querySelector('.bcv-stats');
  res.statCols = stats ? getComputedStyle(stats).gridTemplateColumns.split(' ').length : 0;
  res.statsW = stats ? Math.round(stats.getBoundingClientRect().width) : 0;
  return res;
};
// the Dashboard's counters: three across where three of at least 210px fit (12px gaps), else two, else one — the CSS's own rule
const colsFor = (w) => Math.max(1, Math.min(3, Math.floor((w + 12) / 222)));

async function withContext(c, fn) {
  const userDataDir = join(tmpdir(), `bcv-zoom-profile-${Date.now()}-${Math.random().toString(36).slice(2, 6)}`);
  const context = await chromium.launchPersistentContext(userDataDir, { channel: 'chromium', headless: true, viewport: { width: c.cw, height: c.ch }, deviceScaleFactor: c.z, args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`] });
  try {
    let [sw] = context.serviceWorkers();
    if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
    await afterMigration(sw); // (the background's setup migration first, or it clears the flags written next)
    await new Promise((r) => setTimeout(r, 900));
    for (const t of context.pages()) if (t.url().endsWith('/setup/setup.html')) await t.close();
    await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'tools:welcomed': true, 'setup:flow': 3, 'whatsnew:seen': v, 'tools:pins': ['calc'] }), manifest.version);
    const page = await context.newPage();
    page.on('pageerror', (e) => failures.push(`page error at ${c.label}: ${e.message}`));
    const settle = async () => {
      await page.waitForFunction(() => { const k = document.documentElement.classList; return !k.contains('bcv-on') || k.contains('bcv-settled'); }, null, { timeout: 15000 }).catch(() => {});
      await page.evaluate(() => Promise.race([Promise.all(document.getAnimations().filter((a) => a.playState === 'running' && a.effect?.getTiming?.().iterations !== Infinity).map((a) => a.finished.catch(() => {}))), new Promise((r) => setTimeout(r, 900))])).catch(() => {});
      await page.waitForTimeout(150);
    };
    const go = async (path) => { await page.goto(`${BASE}${path}`, { waitUntil: 'commit' }).catch(() => {}); await settle(); };
    const measure = async (shot) => {
      const m = await page.evaluate(MEASURE).catch((e) => ({ error: e.message }));
      await page.screenshot({ path: join(OUT, `zoom-${shot}@${c.label}.png`) }).catch(() => {});
      const bad = m.error || m.sideways || m.wide?.length || m.fixedOff?.length || m.cut?.length || m.overlap?.length || m.narrow?.length;
      check(!bad, `${c.label} (${c.cw}×${c.ch} @${c.z}x) ${shot}: nothing sideways, past the edge, off screen, cut, overlapping or squeezed${bad ? ` — ${JSON.stringify({ sideways: m.sideways, wide: m.wide, fixedOff: m.fixedOff, cut: m.cut, overlap: m.overlap, narrow: m.narrow, error: m.error })}` : ''}`);
      return m;
    };
    await fn({ page, sw, go, settle, measure });
  } catch (e) {
    console.error('crashed:', e?.stack || e);
    failures.push(`crash at ${c.label}: ${e.message}`);
  } finally {
    await context.close().catch(() => {});
    rmSync(userDataDir, { recursive: true, force: true });
  }
}

try {
  for (const c of COMBOS) {
    console.log(`${c.label}: ${c.w}×${c.h} at ${c.z * 100}% → ${c.cw}×${c.ch} CSS px`);
    const phone = c.cw <= 700;
    await withContext(c, async ({ page, sw, go, settle, measure }) => {
      let first = null;
      for (const [name, path] of SCREENS) {
        await go(path);
        const m = await measure(name);
        if (name === 'dashboard') first = m;
      }
      // the tier the width lands in
      check(first.phone === phone, `${c.label}: ${phone ? 'the phone layout' : 'the desktop layout'} at ${c.cw}px (phone=${first.phone})`);
      if (!phone) {
        const compact = c.cw <= 1100;
        check(first.sideW === (compact ? '200px' : '242px'), `${c.label}: the sidebar is ${compact ? 'the compact 200px' : 'the full 242px'} (${first.sideW})`);
        check(first.statCols === colsFor(first.statsW) && first.statsW / first.statCols >= 200, `${c.label}: the Dashboard's counters run ${first.statCols} across in ${first.statsW}px, none squeezed`);
      }
      await go('/');
      if (!phone) {
        await page.fill('.bcv-omni__in', 'dis');
        await page.waitForSelector('.bcv-omni__item', { timeout: 6000 });
        await page.waitForTimeout(500);
        await measure('hub');
        await page.keyboard.press('Escape');
      }
      await page.click(phone ? '.bcv-ph-stat:nth-child(2)' : '.bcv-stat:nth-child(2)');
      await page.waitForSelector('.bcv-sheet', { timeout: 6000 });
      await settle();
      const sheet = await measure('sheet');
      const sheetBox = await page.$eval('.bcv-sheet', (el) => { const r = el.getBoundingClientRect(); return { w: Math.round(r.width), h: Math.round(r.height), inside: r.left >= 0 && r.right <= innerWidth + 1 && r.top >= 0 && r.bottom <= innerHeight + 1 }; });
      check(sheetBox.inside, `${c.label}: the sheet sits inside the window (${sheetBox.w}×${sheetBox.h} in ${sheet.vw}×${sheet.vh})`);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(300);
      // the widest tool popup: the periodic table asks for 1120px and takes what the window has
      await go('/#tools');
      await page.click('.bcv-tool-card[data-tool="ptable"]');
      await page.waitForSelector('.bcv-tool', { timeout: 8000 });
      await settle();
      await measure('tool-ptable');
      const pop = await page.$eval('.bcv-tool', (el) => { const r = el.getBoundingClientRect(); return { w: Math.round(r.width), h: Math.round(r.height), inside: r.left >= 0 && r.right <= innerWidth + 1 && r.top >= 0 && r.bottom <= innerHeight + 1, asked: el.style.getPropertyValue('--bcv-tool-w') }; });
      // (a phone's sheet is a full-width bottom sheet: the overlay has no padding there)
      check(pop.inside && pop.w <= c.cw && pop.w === (phone ? c.cw : Math.min(1120, c.cw - 52)), `${c.label}: the periodic table popup asks for ${pop.asked} and takes ${pop.w}px of ${c.cw}, inside the window`);
      await page.keyboard.press('Escape');
      await page.waitForTimeout(300);
      // What's New over the page
      await sw.evaluate(async () => { await self.BCV.api.storage.local.set({ 'whatsnew:from': '2.7.5' }); await self.BCV.api.storage.local.remove(['whatsnew:seen']); });
      await go('/');
      const wn = await page.waitForFunction(() => document.querySelector('#bcv-whatsnew')?.shadowRoot?.querySelector('.intro')?.hidden === true, null, { timeout: 10000 }).then(() => true).catch(() => false);
      check(wn, `${c.label}: What's New opens over the page`);
      await page.waitForTimeout(400);
      await measure('whatsnew');
      await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'whatsnew:seen': v }), manifest.version);
    });
  }

  // ---- the live switch: the window zoomed while the interface is up ----------------------------------------
  console.log('live zoom');
  await withContext({ label: 'live', w: 1366, h: 620, z: 1, cw: 1366, ch: 620 }, async ({ page, go }) => {
    await go('/');
    await page.setViewportSize({ width: 911, height: 413 }); // 150%: compact, no reload
    await page.waitForTimeout(400);
    const compact = await page.evaluate(() => { const s = document.querySelector('.bcv-stats'); return { side: getComputedStyle(document.documentElement).getPropertyValue('--bcv-side-w').trim(), cols: getComputedStyle(s).gridTemplateColumns.split(' ').length, w: Math.round(s.getBoundingClientRect().width), phone: document.documentElement.classList.contains('bcv-phone') }; });
    check(compact.side === '200px' && compact.cols === colsFor(compact.w) && !compact.phone, `zoomed to 150% in place: the sidebar narrows and the counters re-flow, no reload (${JSON.stringify(compact)})`);
    await page.setViewportSize({ width: 683, height: 310 }); // 200%: across the phone line
    const toPhone = await page.waitForFunction(() => document.documentElement.classList.contains('bcv-phone') && !!document.querySelector('#bcv-tabbar'), null, { timeout: 8000 }).then(() => true).catch(() => false);
    check(toPhone, 'zoomed to 200%: the page loads afresh into the phone layout once the zoom has settled');
    await page.setViewportSize({ width: 1366, height: 620 }); // back to 100%
    const back = await page.waitForFunction(() => !document.documentElement.classList.contains('bcv-phone') && !!document.querySelector('#bcv-side'), null, { timeout: 8000 }).then(() => true).catch(() => false);
    check(back, 'back to 100%: the page loads afresh into the desktop layout');
    await page.screenshot({ path: join(OUT, 'zoom-live-back.png') }).catch(() => {});
  });
} catch (e) {
  console.error('crashed:', e?.stack || e);
  failures.push('crash: ' + e.message);
} finally {
  server.kill();
  rmSync(extDir, { recursive: true, force: true });
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);

#!/usr/bin/env node
// The bar over a tool's own page (extension/content/toolbar.js) never covers that page. The bar's
// script is run on sample tool pages — the extension's background stood in for — with every way a
// site pins something to the window: a header fixed at the top, a sidebar fixed from top to bottom, a
// panel 100vh tall, a header that slides away on scroll, a heading stuck to the top, a layer placed
// against the page's top, an app built to the window's height (html and body at 100%, a root at 100vh,
// a body at least 100vh), a header drawn late by the page's own script — and a plain article, which
// nothing should touch. Each is checked with the bar shown (below it, its foot still in view), tucked
// away (exactly as the site had it), and brought back; and in the dark look, where the page is turned
// over with a filter and nothing may be moved twice.
// Requires Playwright (project or global install).
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import { readFileSync, mkdirSync } from 'node:fs';
import { createServer } from 'node:http';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { fastMotion } from './harness.mjs';

const require = createRequire(import.meta.url);
let chromium;
try {
  ({ chromium } = require('playwright'));
} catch {
  const prefix = execSync('npm root -g').toString().trim();
  ({ chromium } = createRequire(join(prefix, 'x.js'))('playwright'));
}

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const out = join(root, 'scripts', 'dev', 'out');
mkdirSync(out, { recursive: true });
const PORT = 8812;
const BASE = `http://localhost:${PORT}`;
const toolbar = readFileSync(join(root, 'extension', 'content', 'toolbar.js'), 'utf8');

// ---- the sample tool pages ------------------------------------------------------------------
const article = Array.from({ length: 40 }, (_, i) => `<p>Paragraph ${i + 1}. A line of the tool's own text, long enough to wrap once or twice in a window of ordinary width so the page scrolls.</p>`).join('');
const PAGES = {
  pinned: `<style>body{margin:0;font:15px sans-serif} .gone{top:-60px!important}</style>
<header id="t-head" style="position:fixed;top:0;left:0;right:0;height:56px;background:#036;color:#fff;z-index:5">Tool header</header>
<nav id="t-side" style="position:fixed;top:0;bottom:0;left:0;width:120px;background:#e4e8ef;z-index:4">Side<span id="t-side-foot" style="position:absolute;bottom:4px;left:8px">side foot</span></nav>
<aside id="t-panel" style="position:fixed;top:0;right:0;width:140px;height:100vh;background:#d9dde4;z-index:4">Panel<span id="t-panel-foot" style="position:absolute;bottom:4px;left:8px">panel foot</span></aside>
<div id="t-slide" style="position:fixed;top:0;left:200px;width:100px;height:40px;background:#c60;z-index:6">slides</div>
<div id="t-abs" style="position:absolute;top:0;left:330px;width:90px;height:30px;background:#690;z-index:6">placed</div>
<div id="t-modal" style="position:fixed;top:50%;left:50%;transform:translate(-50%,-50%);width:200px;height:120px;background:#fff;border:1px solid #999;z-index:3">centred</div>
<footer id="t-foot" style="position:fixed;bottom:0;left:0;right:0;height:30px;background:#333;color:#fff;z-index:5">fixed footer</footer>
<main style="margin:56px 150px 40px 130px">${article.slice(0, 2000)}<h3 id="t-sticky" style="position:sticky;top:0;margin:0;height:30px;background:#fc0;z-index:2">Sticky heading</h3>${article}</main>`,
  shell: `<style>html,body{height:100%;margin:0;overflow:hidden;font:15px sans-serif} #app{height:100%;display:flex;flex-direction:column} #app>header{height:50px;flex:none;background:#036;color:#fff} #app>main{flex:1;overflow:auto} #t-foot{height:40px;flex:none;background:#333;color:#fff}</style>
<div id="app"><header id="t-head">App header</header><main>${article}</main><footer id="t-foot">app footer</footer></div>`,
  minvh: `<style>body{margin:0;min-height:100vh;display:flex;flex-direction:column;font:15px sans-serif} #t-foot{margin-top:auto;height:40px;background:#333;color:#fff}</style>
<main><p>A short page.</p></main><footer id="t-foot">footer at the foot</footer>`,
  vhroot: `<style>body{margin:0;font:15px sans-serif} #root{height:100vh;display:flex;flex-direction:column} #t-foot{margin-top:auto;height:40px;background:#333;color:#fff}</style>
<div id="root"><p>A root a window tall.</p><footer id="t-foot">root footer</footer></div>`,
  fixedbody: `<style>body{position:fixed;top:0;left:0;right:0;bottom:0;margin:0;display:flex;flex-direction:column;font:15px sans-serif} #t-head{height:50px;flex:none;background:#036;color:#fff} main{flex:1;overflow:auto} #t-foot{height:40px;flex:none;background:#333;color:#fff}</style>
<header id="t-head">Fixed-body app</header><main>${article}</main><footer id="t-foot">fixed-body footer</footer>`,
  plain: `<style>body{font:15px sans-serif;max-width:700px;margin:20px auto}</style><h1>An article</h1>${article}`,
  late: `<style>body{margin:0;font:15px sans-serif}</style><main style="padding:70px 20px">${article.slice(0, 1500)}</main>
<script>setTimeout(function(){var h=document.createElement('div');h.id='t-head';h.style.cssText='position:fixed;top:0;left:0;right:0;height:60px;background:#036;color:#fff';h.textContent='drawn late';document.body.appendChild(h);},900);</script>`,
};
const server = createServer((req, res) => {
  const name = (req.url || '/').split('?')[0].slice(1);
  if (!PAGES[name]) { res.writeHead(404); return res.end('no'); }
  res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' });
  res.end(`<!doctype html><html><head><meta charset="utf-8"><title>${name}</title></head><body>${PAGES[name]}</body></html>`);
});
await new Promise((r) => server.listen(PORT, r));

// ---- the extension's side, stood in for: this tab is a tool's; the look and the tucked bar kept in storage
const stub = (theme) => `(function () {
  var store = { 'ext:theme': ${JSON.stringify(theme)} }, heard = [];
  self.browser = {
    runtime: {
      sendMessage: function (m) {
        if (m && m.type === 'toolTab') return Promise.resolve({ tool: { title: 'Sample tool', state: 'ready' } });
        return Promise.resolve({ ok: false });
      },
      onMessage: { addListener: function () {} },
    },
    storage: {
      local: {
        get: function (keys) { var r = {}; (Array.isArray(keys) ? keys : [keys]).forEach(function (k) { if (k in store) r[k] = store[k]; }); return Promise.resolve(r); },
        set: function (items) { for (var k in items) store[k] = items[k]; return Promise.resolve(); },
      },
      onChanged: { addListener: function (f) { heard.push(f); } },
    },
  };
})();`;

const failures = [];
const check = (cond, label) => {
  console.log(`${cond ? '  ✓' : '  ✗'} ${label}`);
  if (!cond) failures.push(label);
};
const browser = await chromium.launch({ channel: 'chromium' });
const errors = [];
const open = async (name, theme = 'light') => {
  const context = await browser.newContext({ viewport: { width: 1200, height: 760 } });
  fastMotion(context); // every page's animations MOTION_RATE× faster (harness.mjs)
  await context.addInitScript(stub(theme));
  await context.addInitScript(toolbar);
  const page = await context.newPage();
  page.on('pageerror', (e) => errors.push(`${name}: ${e.message}`));
  await page.goto(`${BASE}/${name}`);
  await page.waitForFunction(() => document.querySelector('bcv-tool-bar')?.dataset.state === 'ready', null, { timeout: 10000 });
  await page.waitForTimeout(500);
  return page;
};
const rects = (page, ids) => page.evaluate((list) => Object.fromEntries(list.map((id) => {
  const n = document.getElementById(id);
  const r = n?.getBoundingClientRect();
  return [id, r ? { top: Math.round(r.top), bottom: Math.round(r.bottom), h: Math.round(r.height) } : null];
})), ids);
/** The page's own declarations as the site wrote them: the same page opened without the bar. */
const siteDecl = async (name) => {
  const context = await browser.newContext({ viewport: { width: 1200, height: 760 } });
  fastMotion(context); // every page's animations MOTION_RATE× faster (harness.mjs)
  const page = await context.newPage();
  await page.goto(`${BASE}/${name}`);
  const d = await decl(page);
  await context.close();
  return d;
};
const vh = (page) => page.evaluate(() => window.innerHeight);
const bar = (page, which) => page.evaluate((w) => document.querySelector('bcv-tool-bar').shadowRoot.querySelector(w).click(), which);
// an element's own declarations (property, value, priority), in no particular order: what the site wrote
const decl = (page) => page.evaluate(() => Object.fromEntries([...document.querySelectorAll('[id^="t-"]')].map((n) => [n.id, n.hasAttribute('style') ? [...n.style].map((p) => `${p}:${n.style.getPropertyValue(p)}${n.style.getPropertyPriority(p) ? '!' : ''}`).sort().join(';') : null])));

try {
  console.log('a page with things pinned to the window');
  let page = await open('pinned');
  const H = await vh(page);
  const before = await siteDecl('pinned');
  let r = await rects(page, ['t-head', 't-side', 't-side-foot', 't-panel', 't-panel-foot', 't-slide', 't-abs', 't-modal', 't-foot']);
  check(r['t-head'].top === 52 && r['t-slide'].top === 52 && r['t-abs'].top === 52, `a header fixed at the top, one that slides, and a layer placed at the page's top all start below the bar: ${JSON.stringify({ head: r['t-head'], slide: r['t-slide'], abs: r['t-abs'] })}`);
  check(r['t-side'].top === 52 && r['t-side'].bottom === H && r['t-side-foot'].bottom <= H, `a sidebar fixed from top to bottom starts below the bar and still ends at the window's foot: ${JSON.stringify({ side: r['t-side'], foot: r['t-side-foot'], H })}`);
  check(r['t-panel'].top === 52 && r['t-panel'].bottom === H && r['t-panel-foot'].bottom <= H, `a panel 100vh tall is moved down and made as much shorter, its foot in view: ${JSON.stringify({ panel: r['t-panel'], foot: r['t-panel-foot'], H })}`);
  const after = await decl(page);
  check(r['t-foot'].bottom === H && after['t-modal'] === before['t-modal'] && after['t-foot'] === before['t-foot'], `what the bar does not cover (a centred box, a footer at the foot) is not touched: ${JSON.stringify({ foot: r['t-foot'], modal: r['t-modal'] })}`);
  await page.screenshot({ path: join(out, 'toolbar-01-pinned.png') });
  await page.evaluate(() => document.getElementById('t-slide').classList.add('gone'));
  await page.waitForTimeout(450);
  r = await rects(page, ['t-slide']);
  check(r['t-slide'].bottom <= 52, `the site's own move still works: a header that slides away goes up behind the bar, not pinned in place: ${JSON.stringify(r['t-slide'])}`);
  await page.evaluate(() => document.getElementById('t-slide').classList.remove('gone'));
  await page.evaluate(() => window.scrollTo(0, 900));
  await page.waitForTimeout(500);
  r = await rects(page, ['t-sticky']);
  check(r['t-sticky'].top === 52, `a heading stuck to the top as the page scrolls sticks below the bar: ${JSON.stringify(r['t-sticky'])}`);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.waitForTimeout(300);
  await bar(page, '.hide');
  await page.waitForTimeout(500);
  r = await rects(page, ['t-head', 't-side', 't-panel', 't-slide', 't-abs']);
  const tucked = await decl(page);
  const changed = Object.keys(before).filter((id) => before[id] !== tucked[id]);
  check(r['t-head'].top === 0 && r['t-side'].top === 0 && r['t-panel'].top === 0 && r['t-panel'].h === H && r['t-abs'].top === 0 && changed.length === 0 && (await page.evaluate(() => getComputedStyle(document.documentElement).marginTop)) === '0px',
    `with the bar tucked away every one of them is exactly as the site had it (not one style left behind): ${JSON.stringify({ r, changed, was: before[changed[0]], now: tucked[changed[0]] })}`);
  await page.evaluate(() => window.scrollTo(0, 900));
  await page.waitForTimeout(400);
  r = await rects(page, ['t-sticky']);
  check(r['t-sticky'].top === 0, `and the stuck heading sticks at the very top again: ${JSON.stringify(r['t-sticky'])}`);
  await page.evaluate(() => window.scrollTo(0, 0));
  await bar(page, '.peek');
  await page.waitForTimeout(500);
  r = await rects(page, ['t-head', 't-side', 't-panel']);
  check(r['t-head'].top === 52 && r['t-side'].top === 52 && r['t-panel'].bottom === H, `brought back, the bar makes room again: ${JSON.stringify(r)}`);
  // the look switch turns the page over with a filter on <body>, which fixed layers are then placed
  // against: nothing is moved twice, and turning it back lands where it was
  await bar(page, '.theme');
  await page.waitForTimeout(500);
  r = await rects(page, ['t-head', 't-panel']);
  let d = await decl(page);
  check(r['t-head'].top >= 52 && d['t-head'] === before['t-head'] && d['t-panel'] === before['t-panel'], `turned dark (the page turned over with a filter on <body>, which fixed layers are then placed against), the header is below the bar and nothing is moved twice: ${JSON.stringify({ head: r['t-head'], style: d['t-head'] })}`);
  await bar(page, '.theme');
  await page.waitForTimeout(500);
  r = await rects(page, ['t-head', 't-panel']);
  check(r['t-head'].top === 52 && r['t-panel'].bottom === H, `and light again, exactly as before: ${JSON.stringify(r)}`);
  await page.context().close();

  console.log('pages built to the window\'s height');
  page = await open('shell');
  r = await rects(page, ['t-head', 't-foot']);
  check(r['t-head'].top === 52 && r['t-foot'].bottom === H, `an app with html and body at 100% (nothing scrolls) sits below the bar with its footer in view: ${JSON.stringify(r)}`);
  await page.screenshot({ path: join(out, 'toolbar-02-shell.png') });
  await bar(page, '.hide');
  await page.waitForTimeout(500);
  r = await rects(page, ['t-head', 't-foot']);
  const htmlStyle = await page.evaluate(() => document.documentElement.style.height + '|' + document.body.style.height);
  check(r['t-head'].top === 0 && r['t-foot'].bottom === H && htmlStyle === '|', `tucked away, the app has the whole window again, as it was: ${JSON.stringify({ r, htmlStyle })}`);
  await page.context().close();
  page = await open('minvh');
  r = await rects(page, ['t-foot']);
  const scrolls = await page.evaluate(() => document.scrollingElement.scrollHeight - window.innerHeight);
  check(r['t-foot'].bottom === H && scrolls <= 1, `a short page at least a window tall: its footer at the window's foot, nothing to scroll for: ${JSON.stringify({ foot: r['t-foot'], scrolls })}`);
  await page.context().close();
  page = await open('vhroot');
  r = await rects(page, ['t-foot']);
  check(r['t-foot'].bottom === H, `a root 100vh tall: its footer at the window's foot: ${JSON.stringify(r['t-foot'])}`);
  await page.context().close();

  console.log('a plain page, and a header drawn late');
  page = await open('plain');
  const plain = await page.evaluate(() => ({ styled: [...document.body.querySelectorAll('[style]')].length, body: document.body.getAttribute('style') || '', h1: Math.round(document.querySelector('h1').getBoundingClientRect().top) }));
  check(plain.styled === 0 && plain.body === '' && plain.h1 >= 52, `an ordinary article is only pushed down, nothing in it touched: ${JSON.stringify(plain)}`);
  await page.context().close();
  page = await open('late');
  await page.waitForSelector('#t-head', { timeout: 5000 });
  await page.waitForTimeout(600);
  r = await rects(page, ['t-head']);
  check(r['t-head'].top === 52, `a header the page draws after it has loaded is moved below the bar too: ${JSON.stringify(r['t-head'])}`);
  await page.context().close();

  console.log('the dark look from the start');
  page = await open('pinned', 'dark');
  r = await rects(page, ['t-head', 't-side']);
  d = await decl(page);
  const site = await siteDecl('pinned');
  check(r['t-head'].top >= 52 && r['t-side'].top >= 52 && d['t-head'] === site['t-head'] && d['t-side'] === site['t-side'], `opened dark, the pinned header and sidebar are below the bar without being moved: ${JSON.stringify({ r, head: d['t-head'] })}`);
  await page.context().close();
  page = await open('shell', 'dark');
  r = await rects(page, ['t-head', 't-foot']);
  check(r['t-head'].top === 52 && r['t-foot'].bottom === H, `an app built to the window's height, dark: below the bar, its footer in view: ${JSON.stringify(r)}`);
  await page.context().close();
  for (const theme of ['light', 'dark']) {
    page = await open('fixedbody', theme);
    r = await rects(page, ['t-head', 't-foot']);
    check(r['t-head'].top === 52 && r['t-foot'].bottom === H, `an app whose <body> is itself fixed to the window (${theme}): below the bar, its footer in view: ${JSON.stringify(r)}`);
    await page.context().close();
  }
  check(errors.length === 0, `no page errors${errors.length ? `: ${errors.slice(0, 3).join(' | ')}` : ''}`);
} catch (e) {
  check(false, `the suite ran to the end: ${e.message}`);
} finally {
  await browser.close();
  server.close();
}
if (failures.length) {
  console.log(`\n${failures.length} check(s) failed:\n${failures.map((f) => ` - ${f}`).join('\n')}`);
  process.exit(1);
}
console.log('\nall passed');

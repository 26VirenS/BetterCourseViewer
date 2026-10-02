#!/usr/bin/env node
// The iPhone app's side of the interface, without an iPhone: the extension injected the way
// ios/SimplCourses/Web/ScriptBundle.swift injects it, at a phone's size with touch, against the mock
// Canvas, with a stand-in for the app's native half (webkit.messageHandlers.bcv: its storage, its
// alert and action sheet, its file viewer, its swipe-back switch). It checks what the page asks of
// the app and what it no longer draws itself; the sheets' drag by touch; Back walking the history
// rather than piling onto it; the course chips on every course screen; the viewport the app sets; and
// the sign-in reader (Web/login.js) against sample school sign-in pages — what it finds, what it says,
// and how it fills and presses a form.
// Requires Playwright (project or global install).
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { readFileSync, mkdirSync } from 'node:fs';
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
const ext = join(root, 'extension');
const out = join(root, 'scripts', 'dev', 'out');
mkdirSync(out, { recursive: true });
const PORT = 8794;
const BASE = `http://localhost:${PORT}`;
const HOST = 'localhost';

// ---- the bundle, built the way the app builds it --------------------------------------------
const manifest = JSON.parse(readFileSync(join(ext, 'manifest.json'), 'utf8'));
const file = (rel) => readFileSync(join(ext, rel), 'utf8');
const swift = (rel) => readFileSync(join(root, 'ios', 'SimplCourses', rel), 'utf8');
const guardJS = `if(location.hostname!==${JSON.stringify(HOST)})return;`;
const seen = new Set();
const wrap = (rel) => {
  if (seen.has(rel)) return '';
  seen.add(rel);
  return `(function(){${guardJS}try{\n${file(rel)}\n}catch(e){console.error('[Simpl Courses] ${rel} failed',e)}})();\n`;
};
const bridge = swift('Web/bridge.js').split('__MANIFEST__').join(JSON.stringify(manifest));
// the viewport the app gives Canvas's pages, read from ScriptBundle.swift itself
const viewport = (swift('Web/ScriptBundle.swift').match(/static let viewport = "([^"]+)"/) || [])[1] || '';
const start = [`(function(){${guardJS}\n${bridge}\n})();\n`, `(function(){${guardJS}var m=document.querySelector('meta[name=viewport]');if(!m){m=document.createElement('meta');m.name='viewport';(document.head||document.documentElement).appendChild(m);}m.content=${JSON.stringify(viewport)};})();\n`];
const end = [];
for (const rel of manifest.background?.scripts || ['lib/settings.js', 'lib/devcode.js', 'background.js']) start.push(wrap(rel));
const css = [];
for (const cs of manifest.content_scripts || []) {
  if ((cs.js || []).includes('content/sniff.js')) continue;
  const list = cs.run_at === 'document_start' ? start : end;
  for (const rel of cs.js || []) list.push(wrap(rel));
  for (const rel of cs.css || []) css.push(file(rel));
}
start.splice(1, 0, `(function(){${guardJS}var s=document.createElement('style');s.id='bcv-css';s.textContent=${JSON.stringify(css.join('\n'))};(document.head||document.documentElement).appendChild(s);})();\n`);
end.push(`(function(){var m=document.querySelector('meta[name=viewport]');if(m&&location.hostname===${JSON.stringify(HOST)})m.content=${JSON.stringify(viewport)};})();\n`);
end.push("(function(){var s=document.createElement('style');s.textContent='html{touch-action:manipulation}';(document.head||document.documentElement).appendChild(s);})();\n");
end.push('(function(){var s=document.getElementById(\'bcv-css\');if(s&&document.body)document.body.appendChild(s);})();\n');
const initScript = `(function () {
  function start() {\n${start.join('\n')}\n}
  if (document.documentElement) start();
  else new MutationObserver(function (m, o) { if (document.documentElement) { o.disconnect(); start(); } }).observe(document, { childList: true });
  document.addEventListener('DOMContentLoaded', function () {\n${end.join('\n')}\n});
})();`;

// ---- the app's native half, stood in for: storage (with its broadcast), the alert, the action sheet,
// the file viewer and the swipe-back switch, every call kept for the checks
const setupFlow = Number((file('background.js').match(/const SETUP_FLOW = (\d+)/) || [])[1]) || 0;
const nativeStub = `(function () {
  var KEY = 'bcv:native-store';
  var load = function () { try { return JSON.parse(localStorage.getItem(KEY) || '{}') || {}; } catch (e) { return {}; } };
  var save = function (d) { try { localStorage.setItem(KEY, JSON.stringify(d)); } catch (e) {} };
  if (!localStorage.getItem(KEY)) save({ 'setup:flow': ${setupFlow}, 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'tools:welcomed': true, 'whatsnew:seen': ${JSON.stringify(manifest.version)} });
  var calls = self.__nativeCalls = [];
  self.__nativeAnswers = { ask: true, menu: 0 };
  var tell = function (changes) { setTimeout(function () { try { self.BCVBridge && self.BCVBridge.storageChanged(changes); } catch (e) {} }, 0); };
  self.webkit = { messageHandlers: { bcv: { postMessage: function (msg) {
    calls.push(JSON.parse(JSON.stringify(msg)));
    var d = load(), changes = {}, k;
    switch (msg.op) {
      case 'storage.get': {
        var keys = msg.keys;
        if (keys == null) return Promise.resolve(d);
        var list = Array.isArray(keys) ? keys : (typeof keys === 'object' ? Object.keys(keys) : [keys]);
        var res = {};
        list.forEach(function (key) { if (key in d) res[key] = d[key]; else if (keys && typeof keys === 'object' && !Array.isArray(keys)) res[key] = keys[key]; });
        return Promise.resolve(res);
      }
      case 'storage.set':
        for (k in msg.items) { changes[k] = { oldValue: d[k], newValue: msg.items[k] }; d[k] = msg.items[k]; }
        save(d); tell(changes); return Promise.resolve(null);
      case 'storage.remove':
        (msg.keys || []).forEach(function (key) { if (key in d) { changes[key] = { oldValue: d[key] }; delete d[key]; } });
        save(d); tell(changes); return Promise.resolve(null);
      case 'storage.clear': save({}); return Promise.resolve(null);
      case 'ask': return Promise.resolve({ ok: !!self.__nativeAnswers.ask });
      case 'menu': return Promise.resolve({ index: self.__nativeAnswers.menu });
      case 'previewFile': return Promise.resolve({ ok: true });
      default: return Promise.resolve(null);
    }
  } } } };
})();`;

// ---- run ------------------------------------------------------------------------------------
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));
const failures = [];
const check = (cond, label) => {
  console.log(`${cond ? '  ✓' : '  ✗'} ${label}`);
  if (!cond) failures.push(label);
};
const browser = await chromium.launch({ channel: 'chromium' });
const context = await browser.newContext({ viewport: { width: 402, height: 874 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
fastMotion(context); // every page's animations MOTION_RATE× faster (harness.mjs)
await context.addInitScript(nativeStub);
await context.addInitScript(initScript);
let page = null;
const errors = [];
const calls = (op) => page.evaluate((o) => self.__nativeCalls.filter((c) => !o || c.op === o), op);
const lastNav = async () => (await calls('navState')).pop()?.canSwipeBack;
const eventually = async (fn, ms = 6000) => { const t = Date.now(); while (Date.now() - t < ms) { if (await fn().catch(() => false)) return true; await new Promise((r) => setTimeout(r, 120)); } return false; };
try {
  page = await context.newPage();
  page.on('pageerror', (e) => errors.push(e.message));
  const cdp = await context.newCDPSession(page);
  /** A finger: down at (x, y), moved by dy in steps, up — what a drag on the phone sends. */
  const touchDrag = async (x, y, dy, { steps = 8, ms = 14 } = {}) => {
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [{ x, y }] });
    for (let i = 1; i <= steps; i++) {
      await cdp.send('Input.dispatchTouchEvent', { type: 'touchMove', touchPoints: [{ x, y: y + (dy * i) / steps }] });
      await new Promise((r) => setTimeout(r, ms));
    }
    await cdp.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  };
  const texts = (sel) => page.$$eval(sel, (els) => els.map((e) => (e.innerText || e.textContent).replace(/\s+/g, ' ').trim()));

  console.log('the app\'s screen');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-tabbar', { timeout: 20000 });
  const vp = await page.evaluate(() => ({ phone: document.documentElement.classList.contains('bcv-phone'), meta: document.querySelector('meta[name=viewport]')?.content, touch: getComputedStyle(document.documentElement).touchAction }));
  check(vp.phone && /maximum-scale=1/.test(vp.meta || '') && /user-scalable=no/.test(vp.meta || '') && /viewport-fit=cover/.test(vp.meta || '') && vp.touch === 'manipulation', `the page is the app's screen, never zoomed: the viewport the app sets (no pinch, no zoom into a field) and a double tap that is two taps: ${JSON.stringify(vp)}`);
  check(await eventually(async () => (await lastNav()) === false), `on a tab's root screen the phone's own swipe-back is off (nothing to go back to): ${JSON.stringify(await calls('navState'))}`);

  console.log('course navigation');
  await page.goto(`${BASE}/courses/101`);
  await page.waitForSelector('.bcv-ph-body--course', { timeout: 20000 });
  const home = await page.evaluate(() => ({ chips: [...document.querySelectorAll('.bcv-ph-tab')].map((t) => t.dataset.tab), active: document.querySelector('.bcv-ph-tab.is-active')?.dataset.tab, top: Math.round(document.querySelector('.bcv-ph-chips')?.getBoundingClientRect().top || 9999), list: Math.round(document.querySelector('.bcv-ph-links')?.getBoundingClientRect().top || 0) }));
  check(home.chips.length >= 5 && home.active === 'home' && home.top < home.list && home.top < 400, `a course's Home carries the section chips at the top, Home lit, before the open work (not only the list at the end): ${JSON.stringify(home)}`);
  check(await eventually(async () => (await lastNav()) === true), 'a pushed screen has the phone\'s own swipe-back on');
  await page.click('.bcv-ph-tab[data-tab="files"]');
  await page.waitForFunction(() => /\/courses\/101\/files/.test(location.pathname), null, { timeout: 15000 });
  await page.waitForTimeout(400);
  const files = await page.evaluate(() => { const row = document.querySelector('.bcv-ph-chips'); const a = row?.querySelector('.is-active'); const r = row?.getBoundingClientRect(), q = a?.getBoundingClientRect(); return { active: a?.dataset.tab, inView: !!(r && q && q.left >= r.left - 1 && q.right <= r.right + 1), depth: history.state?.d, len: history.length }; });
  check(files.active === 'files' && files.inView && files.depth === 1, `a chip opens its section in place, the chip lit and brought into view, the entry counted as a move within the page: ${JSON.stringify(files)}`);
  await page.click('.bcv-topbar__back');
  await page.waitForFunction(() => location.pathname === '/courses/101', null, { timeout: 15000 });
  const backed = await page.evaluate(() => ({ len: history.length, depth: history.state?.d ?? 0 }));
  check(backed.len === files.len && backed.depth === 0, `Back goes back through the history (no new entry piled on, so the phone's swipe-back and Back agree): ${JSON.stringify({ before: files.len, after: backed })}`);
  await page.click('.bcv-ph-tab[data-tab="assignments"]');
  await page.waitForFunction(() => /\/assignments$/.test(location.pathname), null, { timeout: 15000 });
  const before = page.url();
  // the pointer events of a finger from the left edge across (sent as events: a swipe, never a tap on what is under it)
  const edgeSwipe = () => page.evaluate(() => {
    const at = document.elementFromPoint(6, 500) || document.body;
    const ev = (type, x, y) => at.dispatchEvent(new PointerEvent(type, { bubbles: true, cancelable: true, composed: true, pointerType: 'touch', isPrimary: true, clientX: x, clientY: y }));
    ev('pointerdown', 6, 500);
    for (let i = 1; i <= 8; i++) ev('pointermove', 6 + 18 * i, 500 + i);
    ev('pointerup', 150, 508);
  });
  await edgeSwipe();
  await page.waitForTimeout(600);
  check(page.url() === before, 'in the app the page\'s own edge swipe stands down (the phone\'s own swipe-back is the one; one swipe is one Back)');
  // (the same swipe without the app's swipe-back is the page's own Back: the check above is not idle)
  await page.evaluate(() => { self.__navState = self.BCVBridge.native.navState; delete self.BCVBridge.native.navState; });
  await edgeSwipe();
  const swiped = await eventually(async () => page.url() !== before, 4000);
  await page.evaluate(() => { self.BCVBridge.native.navState = self.__navState; });
  check(swiped, `without the phone's own swipe-back, the same swipe is the page's Back: ${page.url()}`);

  console.log('the phone\'s own alert, action sheet and file viewer');
  const asked = await page.evaluate(async () => { const r = await self.BCV.ui.askSheet({ title: 'Delete this task?', note: 'It goes from your planner.', okLabel: 'Delete', danger: true }); return { r, drawn: !!document.querySelector('.bcv-ask-ov') }; });
  const askCall = (await calls('ask')).pop();
  check(asked.r === true && !asked.drawn && askCall?.title === 'Delete this task?' && askCall?.okLabel === 'Delete' && askCall?.danger === true && askCall?.cancelLabel === 'Cancel', `a question is the phone's own alert (its answer comes back; nothing drawn): ${JSON.stringify({ asked, askCall })}`);
  const picked = await page.evaluate(async () => {
    self.__picked = null;
    const at = document.querySelector('.bcv-topbar__back');
    self.BCV.ui.menu(at, [{ label: 'High', sub: 'Do first', onSelect: () => { self.__picked = 'High'; } }, { label: 'Low', active: true, onSelect: () => { self.__picked = 'Low'; } }]);
    await new Promise((r) => setTimeout(r, 120));
    return { picked: self.__picked, drawn: !!document.querySelector('.bcv-menu') };
  });
  const menuCall = (await calls('menu')).pop();
  check(picked.picked === 'High' && !picked.drawn && menuCall?.items?.map((i) => i.label).join(',') === 'High,Low' && menuCall.items[1].active === true && menuCall.items[0].sub === 'Do first' && Number.isFinite(menuCall.rect?.x), `a menu is the phone's own action sheet (the pick runs its action; nothing drawn): ${JSON.stringify({ picked, menuCall })}`);
  const pickerPick = await page.evaluate(async () => {
    let got = null;
    const p = self.BCV.ui.picker([{ value: 'a', text: 'Alpha' }, { value: 'b', text: 'Beta' }], 'b', (v) => { got = v; }, { label: 'Letter' });
    document.body.append(p);
    self.__nativeAnswers.menu = 0;
    p.click();
    await new Promise((r) => setTimeout(r, 150));
    p.remove();
    return { got, drawn: !!document.querySelector('.bcv-picker__list') };
  });
  const pickerCall = (await calls('menu')).pop();
  check(pickerPick.got === 'a' && !pickerPick.drawn && pickerCall?.title === 'Letter' && pickerCall.items.map((i) => `${i.label}${i.active ? '*' : ''}`).join(',') === 'Alpha,Beta*', `a picker is the phone's own action sheet too, the current choice ticked: ${JSON.stringify({ pickerPick, pickerCall })}`);
  await page.evaluate(() => self.BCV.viewer.open({ id: 'f1', display_name: 'Syllabus.pdf', url: '/files/f1/download?download_frd=1', 'content-type': 'application/pdf' }));
  await page.waitForTimeout(200);
  const prev = (await calls('previewFile')).pop();
  check(prev?.url === `${BASE}/files/f1/download?download_frd=1` && prev?.name === 'Syllabus.pdf' && !(await page.$('.bcv-viewer-ov')), `a file opens in the phone's own viewer (the address whole, the name with it), not the drawn sheet: ${JSON.stringify(prev)}`);

  console.log('sheets held like the phone\'s own');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-tabbar', { timeout: 20000 });
  await page.waitForTimeout(500);
  await page.evaluate(() => self.BCV.phone.openSheet({ title: 'Many', rows: Array.from({ length: 40 }, (_, i) => ({ label: `Row ${i + 1}`, note: 'A row', onSelect: () => {} })) }));
  await page.waitForSelector('.bcv-sheet-ov .bcv-ph-sheet', { timeout: 5000 });
  await page.waitForTimeout(500);
  check(await eventually(async () => (await lastNav()) === false), 'with a sheet up the phone\'s own swipe-back is off (the swipe is the sheet\'s)');
  const s0 = await page.evaluate(() => { const r = document.querySelector('.bcv-ph-sheet').getBoundingClientRect(); const l = document.querySelector('.bcv-ph-sheet__list').getBoundingClientRect(); return { top: Math.round(r.top), h: Math.round(r.height), listY: Math.round(l.top + 40), x: Math.round(l.left + l.width / 2) }; });
  await touchDrag(s0.x, s0.listY + 60, -160);
  await page.waitForTimeout(600);
  const s1 = await page.evaluate(() => { const s = document.querySelector('.bcv-ph-sheet'); const r = s.getBoundingClientRect(); return { large: s.classList.contains('is-large'), top: Math.round(r.top), h: Math.round(r.height) }; });
  check(s1.large && s1.top < s0.top - 40 && s1.h > s0.h, `a sheet holding more than it shows grows to nearly the full screen when pulled up from its list: ${JSON.stringify({ s0, s1 })}`);
  await page.screenshot({ path: join(out, 'ios-01-sheet-large.png') });
  const handle = await page.evaluate(() => { const r = document.querySelector('.bcv-ph-sheet__handle').getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }; });
  await touchDrag(handle.x, handle.y, 110, { steps: 6, ms: 40 });
  await page.waitForTimeout(600);
  const s2 = await page.evaluate(() => { const s = document.querySelector('.bcv-ph-sheet'); return s ? { large: s.classList.contains('is-large'), there: true } : { there: false }; });
  check(s2.there && !s2.large, `pulled down a little from nearly the full screen, it comes back to its own height and stays up: ${JSON.stringify(s2)}`);
  const h2 = await page.evaluate(() => { const r = document.querySelector('.bcv-ph-sheet__handle').getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) }; });
  await touchDrag(h2.x, h2.y, 300, { steps: 6, ms: 30 });
  check(await eventually(async () => !(await page.$('.bcv-sheet-ov')), 3000), 'pulled down far by its grabber, it goes, its own close run');
  // a sheet drawn without a grabber (a question, a prompt) gets one, and is put away the same way
  await page.evaluate(() => { self.__saved = null; self.BCV.ui.promptSheet({ title: 'Rename', value: 'x', onSave: (v) => { self.__saved = v; } }); });
  await page.waitForSelector('.bcv-sheet-ov .bcv-sheet--prompt', { timeout: 5000 });
  await page.waitForTimeout(400);
  const grip = await page.evaluate(() => { const g = document.querySelector('.bcv-sheet--prompt > .bcv-ph-sheet__handle--added'); const r = g?.getBoundingClientRect(); return g ? { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) } : null; });
  check(!!grip, 'a sheet drawn without a grabber gets one on the phone');
  if (grip) {
    await touchDrag(grip.x, grip.y, 260, { steps: 6, ms: 30 });
    check(await eventually(async () => !(await page.$('.bcv-sheet-ov')), 3000) && (await page.evaluate(() => self.__saved)) === null, 'and a pull down puts it away, as its own Cancel would (nothing saved)');
  }
  // a pull down at the top of a list moves the sheet; a list scrolled down scrolls back first
  await page.evaluate(() => self.BCV.phone.openSheet({ title: 'Many', rows: Array.from({ length: 40 }, (_, i) => ({ label: `Row ${i + 1}`, onSelect: () => {} })) }));
  await page.waitForSelector('.bcv-sheet-ov .bcv-ph-sheet', { timeout: 5000 });
  await page.waitForTimeout(500);
  const lst = await page.evaluate(() => { const l = document.querySelector('.bcv-ph-sheet__list'); const sc = [l, l.parentElement, document.querySelector('.bcv-ph-sheet')].find((n) => n && n.scrollHeight > n.clientHeight + 1); if (sc) sc.scrollTop = 120; const r = l.getBoundingClientRect(); return { x: Math.round(r.left + r.width / 2), y: Math.round(r.top + 60), scrolled: sc ? sc.scrollTop : 0 }; });
  await touchDrag(lst.x, lst.y, 90, { steps: 6, ms: 30 });
  await page.waitForTimeout(500);
  check(!!(await page.$('.bcv-sheet-ov')), `a pull down on a list scrolled down scrolls it (the sheet stays): ${JSON.stringify(lst)}`);
  await page.evaluate(() => { for (const n of document.querySelectorAll('.bcv-ph-sheet, .bcv-ph-sheet *')) n.scrollTop = 0; });
  await touchDrag(lst.x, lst.y, 280, { steps: 6, ms: 30 });
  check(await eventually(async () => !(await page.$('.bcv-sheet-ov')), 3000), 'and at the top of the list, a pull down takes the sheet away');
  check(errors.length === 0, `no page errors${errors.length ? `: ${errors.slice(0, 3).join(' | ')}` : ''}`);

  // ---- the sign-in reader ---------------------------------------------------------------------
  console.log('the sign-in reader (login.js)');
  const reader = swift('Web/login.js').split('__CANVAS_HOST__').join(JSON.stringify(HOST));
  const sso = await browser.newContext({ viewport: { width: 402, height: 874 }, isMobile: true, hasTouch: true });
  fastMotion(sso); // every page's animations MOTION_RATE× faster (harness.mjs)
  const posted = [];
  const pages = {
    full: '<form method="post" action="/login/submit"><label>NetID <input name="j_username" type="text"></label><label>Password <input name="j_password" type="password"></label><button type="submit">Log in</button></form>',
    twoStep: '<form id="f" onsubmit="event.preventDefault(); document.getElementById(\'p\').hidden=false; document.getElementById(\'u\').readOnly=true;"><input id="u" name="loginfmt" type="email" autocomplete="username"><div id="p" hidden><input name="passwd" type="password"></div><input type="submit" value="Next"></form>',
    register: '<form><input name="email" type="email"><input name="pw1" type="password"><input name="pw2" type="password"><button>Create account</button></form>',
    code: '<form><label>Enter the code we sent <input name="code" inputmode="numeric"></label><button>Verify</button></form>',
    error: '<div class="alert-danger">Invalid username or password.</div><form method="post" action="/login/submit"><input name="username" type="text"><input name="password" type="password"><button type="submit">Sign in</button></form>',
  };
  // every page with the app's reader in it (as the app adds it to every page) and a stand-in for the app's ear
  const ssoPage = (body) => `<!doctype html><html><head><title>School sign-in</title></head><body>${body}<script>self.__said=[];self.webkit={messageHandlers:{bcvLogin:{postMessage:function(m){self.__said.push(m);}}}};<\/script><script>${reader.replace(/<\/script/g, '<\\/script')}<\/script></body></html>`;
  await sso.route('https://sso.example.edu/**', async (route) => {
    const req = route.request();
    if (req.method() === 'POST') { posted.push(req.postData()); return route.fulfill({ contentType: 'text/html', body: ssoPage('<p>Duo: approve the push on your phone.</p>') }); }
    const kind = new URL(req.url()).searchParams.get('k') || 'full';
    return route.fulfill({ contentType: 'text/html', body: ssoPage(pages[kind]) });
  });
  const p2 = await sso.newPage();
  const said = () => p2.evaluate(() => self.__said || []);
  const open = async (k) => { await p2.goto(`https://sso.example.edu/idp/login?k=${k}`); await p2.waitForLoadState('load'); await p2.waitForTimeout(1100); return said(); };
  let m = await open('full');
  check(m.length === 1 && m[0].kind === 'full' && m[0].host === 'sso.example.edu' && m[0].secure === true && m[0].error === '' && m[0].title === 'School sign-in', `a sign-in page with a username and password: said once, as a full form: ${JSON.stringify(m)}`);
  const filled = await p2.evaluate(() => self.SimplLogin.fill({ user: 'jdoe', pass: 's3cret!' }));
  await p2.waitForLoadState('load');
  await p2.waitForTimeout(1100);
  check(filled.done === 'full' && filled.pressed === true && posted.pop() === 'j_username=jdoe&j_password=s3cret%21', `filled as typing would, its own button pressed, and the form sent with them: ${JSON.stringify(filled)}`);
  m = await said();
  check(m.length === 1 && m[0].kind === 'none', `the page after it (a two-factor prompt) says there is nothing to fill: ${JSON.stringify(m)}`);
  m = await open('twoStep');
  check(m.length === 1 && m[0].kind === 'user', `the first step of a two-step sign-in: a username alone: ${JSON.stringify(m)}`);
  const step = await p2.evaluate(() => self.SimplLogin.fill({ user: 'jdoe@school.edu', pass: 'pw' }));
  await p2.waitForTimeout(700);
  m = await said();
  const typedIn = await p2.evaluate(() => document.getElementById('u').value);
  check(step.done === 'user' && step.pressed && typedIn === 'jdoe@school.edu' && m.length === 2 && m[1].kind === 'pass', `the username typed and Next pressed; the password step that follows is said in its turn: ${JSON.stringify({ step, typedIn, m })}`);
  m = await open('register');
  check(m.length === 1 && m[0].kind === 'none', `two password fields (a new password being set) are not a sign-in: ${JSON.stringify(m)}`);
  m = await open('code');
  check(m.length === 1 && m[0].kind === 'none', `a code page is not a sign-in either (it is shown, to be finished by hand): ${JSON.stringify(m)}`);
  m = await open('error');
  check(m.length === 1 && m[0].kind === 'full' && /Invalid username or password/.test(m[0].error), `the sign-in shown again with an error carries the error's words: ${JSON.stringify(m)}`);
  // a press that leaves the same form standing while the page works is not news; an error that then shows is
  await p2.evaluate(() => { const f = document.querySelector('form'); f.addEventListener('submit', (e) => { e.preventDefault(); setTimeout(() => { const d = document.createElement('div'); d.className = 'login-error'; d.textContent = 'Your password is incorrect.'; f.before(d); }, 600); }); document.querySelector('.alert-danger').remove(); });
  await p2.waitForTimeout(500);
  const before2 = (await said()).length;
  await p2.evaluate(() => self.SimplLogin.fill({ user: 'a', pass: 'b' }));
  await p2.waitForTimeout(350);
  const mid = (await said()).length;
  await p2.waitForTimeout(900);
  m = await said();
  check(mid === before2 && m.length === before2 + 1 && /incorrect/.test(m[m.length - 1].error), `the form busy with the press is not said again; the error it then shows is: ${JSON.stringify({ before2, mid, last: m[m.length - 1] })}`);
  // Canvas's own pages are left alone (apart from its login)
  const p3 = await context.newPage();
  await p3.addInitScript(() => { self.__said = []; self.webkit = self.webkit || { messageHandlers: {} }; self.webkit.messageHandlers.bcvLogin = { postMessage: (x) => self.__said.push(x) }; });
  await p3.goto(`${BASE}/courses`);
  await p3.addScriptTag({ content: reader });
  await p3.waitForTimeout(1200);
  check((await p3.evaluate(() => self.__said.length)) === 0 && (await p3.evaluate(() => typeof self.SimplLogin)) === 'undefined', 'on Canvas\'s own pages (other than its login) the reader does nothing at all');
  await p3.close();
  await sso.close();
} catch (e) {
  console.error('ios test crashed:', e);
  failures.push(`crash: ${e.message}`);
  const state = await page?.evaluate(() => ({ url: location.href, html: document.documentElement.className, sheets: document.querySelectorAll('.bcv-sheet-ov').length })).catch(() => null);
  if (state) console.error('page state:', JSON.stringify(state));
} finally {
  await browser.close();
  server.kill();
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n${failures.map((f) => ` - ${f}`).join('\n')}` : '\nall passed');
process.exit(failures.length ? 1 : 0);

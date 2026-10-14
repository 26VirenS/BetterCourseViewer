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
import { appBundle } from './app-bundle.mjs';

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
const PORT = 8794;
const BASE = `http://localhost:${PORT}`;
const HOST = 'localhost';

// ---- the bundle, built the way the app builds it, and the app's native half stood in for (app-bundle.mjs) --------
const { manifest, swift, initScript, shellScript, nativeStub } = appBundle({ root, host: HOST });

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
// (Mac 1.2.13) the app's copy of the extension, for the converter's libraries (the native stand-in's inject)
const vendorFiles = (ctx) => ctx.route('https://simpl-vendor.test/**', (route) => {
  const rel = decodeURIComponent(new URL(route.request().url()).pathname.slice(1));
  if (rel.includes('..') || !(rel.startsWith('lib/vendor/') || rel === 'content/app/tools/office.js')) return route.fulfill({ status: 404, body: '' });
  try { return route.fulfill({ status: 200, contentType: 'text/javascript', headers: { 'access-control-allow-origin': '*' }, body: readFileSync(join(root, 'extension', rel), 'utf8') }); } catch { return route.fulfill({ status: 404, body: '' }); }
});
await vendorFiles(context);
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

  // ---- the app's own chrome (2.99): the page as the engine under Apple's tab bar and screens ----
  console.log('the app\'s own chrome (native shell)');
  const shellCtx = await browser.newContext({ viewport: { width: 402, height: 874 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
  fastMotion(shellCtx);
  await shellCtx.addInitScript(nativeStub);
  await shellCtx.addInitScript(shellScript); // (the app fills the placeholder in: the shell is on)
  await vendorFiles(shellCtx);
  const sp = await shellCtx.newPage();
  const shellErrors = [];
  sp.on('pageerror', (e) => shellErrors.push(e.message));
  const told = (op) => sp.evaluate((o) => self.__nativeCalls.filter((c) => c.op === o), op);
  const nc = (name, args) => sp.evaluate(([n, a]) => self.BCVNative.call(n, a), [name, args || {}]);
  await sp.goto(`${BASE}/`);
  await sp.waitForFunction(() => document.documentElement.classList.contains('bcv-settled') && !!self.BCVNative, null, { timeout: 25000 });
  const chrome = await sp.evaluate(() => ({ shell: document.documentElement.classList.contains('bcv-native-shell'), tabbar: getComputedStyle(document.querySelector('#bcv-tabbar') || document.body).display, topbar: document.querySelector('#bcv-topbar') ? getComputedStyle(document.querySelector('#bcv-topbar')).display : 'none' }));
  const st = (await told('shell.state')).pop();
  check(chrome.shell && chrome.tabbar === 'none' && chrome.topbar === 'none' && st?.shell === true && st?.signedIn === true, `with the app's shell the page draws neither bar and tells the app it is ready, signed in: ${JSON.stringify({ chrome, st })}`);
  const today = await nc('today');
  check(!today.error && today.counters?.length === 6 && today.counters.map((c) => c.key).join() === 'today,next,unread,overdue,tomorrow,graded' && Array.isArray(today.list?.rows) && today.list.rows.length > 0 && today.list.rows.every((r) => r.id && r.title && r.color && 'done' in r) && !!today.dateLine && today.me?.name, `Today for the app: the six counters, the day's list, the week's load, who is signed in: ${JSON.stringify({ counters: today.counters, heading: today.list?.heading, rows: today.list?.rows?.length, load: today.load?.length, me: today.me?.name })}`);
  const counts = await nc('todayCounts', { kept: false });
  check(Number.isInteger(counts.overdue) && Number.isInteger(counts.graded), `Overdue and Graded counted from the courses' assignments: ${JSON.stringify(counts)}`);
  // (Mac 1.2.18) a row dismissed from Overdue leaves its count at once, and stays out of it
  const odSheet = await nc('todaySheet', { key: 'overdue' });
  const odRow = odSheet.sections.flatMap((s) => s.rows).find((r) => r.clearable && r.key);
  if (odRow) {
    const cleared = await nc('clearOverdue', { key: odRow.key });
    const after = await nc('todayCounts', { kept: false });
    check(cleared.ok && cleared.overdue === counts.overdue - 1 && after.overdue === counts.overdue - 1 && after.overdueNote === cleared.overdueNote, `dismissing an overdue row lowers Overdue's count: ${JSON.stringify({ before: counts.overdue, cleared, after: after.overdue, note: after.overdueNote })}`);
  } else check(counts.overdue === 0, `no overdue row to dismiss only when nothing is overdue: ${JSON.stringify({ counts, odSheet })}`);
  const sheet = await nc('todaySheet', { key: 'next' });
  check(sheet.title === 'Next 7 days' && sheet.sections.length > 0 && sheet.sections[0].rows.every((r) => r.title && r.url), `a counter's list as the app's sheet, split the Dashboard's way: ${JSON.stringify({ title: sheet.title, note: sheet.note, sections: sheet.sections.map((s) => [s.title, s.rows.length]) })}`);
  const cs = await nc('courses');
  const prog = await nc('coursesProgress');
  check(!cs.error && cs.rows.length > 0 && cs.rows.every((r) => r.id && r.code && r.color && r.url && r.scoreText) && Object.keys(prog).length > 0, `Courses: the selected courses with their score, colour and unread count, and each one's progress: ${JSON.stringify({ sub: cs.sub, rows: cs.rows.map((r) => `${r.code} ${r.scoreText}`), prog })}`);
  const td = await nc('todo', { group: 'date' });
  const firstRow = td.sections?.[0]?.rows?.[0];
  check(!td.error && td.group === 'date' && td.sections.length > 0 && !!firstRow?.id && 'pri' in firstRow && Array.isArray(td.repeats) && td.repeats.length > 0, `To Do: the next seven days in sections, each row with its priority; the repeats a task can take: ${JSON.stringify({ sub: td.sub, sections: td.sections.map((s) => `${s.title}:${s.rows.length}`), first: firstRow?.title })}`);
  const done = await nc('complete', { id: firstRow.id, done: true });
  const undone = await nc('complete', { id: firstRow.id, done: false });
  check(done.ok && done.done === true && undone.ok && undone.done === false, `a row ticked from the app is marked done on Canvas's planner, and unticked again: ${JSON.stringify({ done, undone })}`);
  await nc('setPriority', { id: firstRow.id, level: 3 });
  const byPri = await nc('todo', { group: 'priority', showDone: false });
  check(byPri.sections[0]?.title === 'High priority' && byPri.sections[0].rows.some((r) => r.id === firstRow.id && r.pri === 3), `a priority set from the app groups the row under it: ${JSON.stringify(byPri.sections.map((s) => s.title))}`);
  await nc('setPriority', { id: firstRow.id, level: 0 });
  await nc('todo', { group: 'date' });
  const gr = await nc('grades');
  check(!gr.error && gr.rows.length > 0 && gr.rows.some((r) => r.pct !== null && r.letter) && gr.scale?.length === 11 && Number.isFinite(gr.goal), `Grades: the term GPA, each course's score, letter and categories, the scale for what-if: ${JSON.stringify({ gpa: gr.gpa, goal: gr.goal, rows: gr.rows.map((r) => `${r.code} ${r.pctText} ${r.letter || ''} cats:${r.cats.length}`), items: gr.items.length })}`);
  const now = new Date();
  const from = new Date(now.getFullYear(), now.getMonth(), 1); from.setDate(from.getDate() - from.getDay());
  const cal = await nc('calendar', { from: from.toISOString(), to: new Date(from.getTime() + 42 * 864e5).toISOString() });
  check(!cal.error && cal.events.length > 0 && cal.events.every((e) => e.day && e.title && e.color) && cal.calendars.length > 0, `Calendar: a span's events with their day, colour and line, and the calendars to choose from: ${JSON.stringify({ events: cal.events.length, first: cal.events[0]?.title, calendars: cal.calendars.length })}`);
  const nf = await nc('notifications');
  check(!nf.error && nf.total > 0 && nf.days.length > 0 && nf.days[0].rows.every((r) => r.id && r.catLabel && r.title), `Notifications: grouped by day, each with its category: ${JSON.stringify({ total: nf.total, unread: nf.unread, days: nf.days.map((d) => `${d.title}:${d.rows.length}`), cats: nf.cats.map((c) => c.key) })}`);
  const one = nf.days[0].rows[0];
  await nc('notifMark', { ids: [one.id], read: true });
  const nf2 = await nc('notifications');
  check(nf2.days.flatMap((d) => d.rows).find((r) => r.id === one.id)?.read === true && nf2.unread === nf.unread - (one.read ? 0 : 1), 'a notification marked read from the app is read (and counted so)');
  const found = await nc('search', { q: 'lab' });
  check(!found.error && found.groups.length > 0 && found.groups.every((g) => g.rows.every((r) => r.url && r.title)), `search for the app's own field: the box's groups, every row with somewhere to go: ${JSON.stringify(found.groups.map((g) => `${g.title}:${g.rows.length}`))}`);
  const snap = await nc('snapshot');
  check(snap.me?.name && Number.isInteger(snap.notifUnread) && snap.version === manifest.version, `the account and the badges for the app's bar: ${JSON.stringify(snap)}`);
  // (iPhone 1.6.1) the schools the first run's search finds: Instructure's listings and (Mac 1.2.5) D2L's Brightspace ones,
  // each a name and a host
  const schools = JSON.parse(readFileSync(join(root, 'extension', 'data', 'schools.json'), 'utf8')).schools;
  const hostOk = (d) => /^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$/.test(d);
  check(schools.length > 10000 && schools.every((s) => Array.isArray(s) && s.length === 2 && s[0].trim() && hostOk(s[1])) && schools.some(([n, d]) => /Merced/.test(n) && d === 'catcourses.ucmerced.edu') && schools.some(([n, d]) => n === 'Abraham Baldwin Agricultural College' && d === 'abac.view.usg.edu') && schools.filter(([, d]) => d.endsWith('.brightspace.com')).length > 500 && new Set(schools.map((s) => `${s[0]}|${s[1]}`)).size === schools.length, `the school search carries ${schools.length} listings, Canvas and Brightspace, each a name and an address, none twice`);
  // (Mac 1.2.13) the hand-in's converter, the web's own: what a PDF-only assignment takes besides PDF, and a text file turned into one
  const convInfo = await nc('convertInfo', { allowed: ['pdf'] });
  const converted = await nc('convertFile', { name: 'notes.txt', type: 'text/plain', data: Buffer.from('Lab notes\nLine two').toString('base64'), allowed: ['pdf'] });
  const pdfHead = converted.data ? Buffer.from(converted.data, 'base64').subarray(0, 5).toString() : '';
  check(!convInfo.error && convInfo.exts.includes('txt') && /^[A-Z].* converted to PDF/.test(convInfo.line) && converted.converted === true && converted.name === 'notes.pdf' && pdfHead === '%PDF-', `the hand-in converts a file of another type to one the assignment takes: ${JSON.stringify({ exts: convInfo.exts, line: convInfo.line, name: converted.name, type: converted.type, head: pdfHead, error: converted.error })}`);
  // (2.99.27) a course's grading scale, as its instructor set it: the course's letter follows it; the standard scale again after
  const gBefore = await nc('grades');
  const course = gBefore.rows.find((r) => r.pct !== null && r.pct > 90 && r.pct < 93) || gBefore.rows.find((r) => r.pct !== null);
  const cut = Math.floor(course.pct) - 1;
  const own = { 'A+': 99, A: cut, 'A-': cut - 2, 'B+': cut - 4, B: cut - 6, 'B-': cut - 8, 'C+': cut - 10, C: cut - 12, 'C-': cut - 14, D: cut - 24 };
  const setOk = await nc('setScale', { id: course.id, scale: own });
  const gOwn = (await nc('grades')).rows.find((r) => r.id === course.id);
  const bad = await nc('setScale', { id: course.id, scale: { ...own, B: own['B+'] + 1 } });
  await nc('setScale', { id: course.id, scale: null });
  const gBack = (await nc('grades')).rows.find((r) => r.id === course.id);
  check(setOk.ok && gOwn.letter === 'A' && gOwn.ownScale === true && gOwn.scale.find((x) => x.letter === 'A').min === cut && !!bad.error && gBack.ownScale === false && gBack.letter === course.letter, `a course's own grading scale letters its score (and a scale out of order is refused): ${JSON.stringify({ pct: course.pct, before: course.letter, own: gOwn.letter, back: gBack.letter, bad: bad.error })}`);
  // (iPhone 1.6) an address the app has no screen for as it stands is opened where it leads: a module item is the item it names
  const viaItem = await nc('resolveUrl', { url: '/courses/102/modules/items/i2' });
  const viaPage = await nc('resolveUrl', { url: '/courses/102/modules/items/i1' });
  check(viaItem.url === '/courses/102/assignments/2001' && viaItem.type === 'Assignment' && viaPage.url === '/courses/102/pages/big-picture', `a module item's address resolves to the item it names, for the app's own screen: ${JSON.stringify({ viaItem, viaPage })}`);
  check((await nc('nope')).error === 'No such call: nope', 'an unknown call is an answer with an error, never a throw across the bridge');
  // (1.2) a course and everything in it, a group, the Inbox — the app draws them itself from these answers
  const ctx = 'courses/101';
  const hm = await nc('home', { ctx });
  check(!hm.error && hm.title && hm.color && hm.sections.some((x) => x.kind === 'assignments') && hm.sections.every((x) => x.kind && x.label) && hm.open.length > 0 && hm.open.every((r) => r.url && r.title && r.sub) && hm.announcements.length > 0 && hm.scoreText, `a course's home for the app: its sections from Canvas's tabs, its open work, its latest announcements, its score: ${JSON.stringify({ title: hm.title, sections: hm.sections.map((x) => x.kind), open: hm.open.length, anns: hm.announcements.length, more: hm.more.map((m) => m.label), score: hm.scoreText })}`);
  const anns = await nc('announcements', { ctx });
  const ann = await nc('topic', { ctx, id: anns.rows[0].id });
  check(!anns.error && anns.rows.length > 0 && anns.rows.every((r) => /\/discussion_topics\/\w+$/.test(r.url) && r.when) && !ann.error && ann.title === anns.rows[0].title && /<p>/.test(ann.html) && !/<script/i.test(ann.html), `announcements, and one opened with its text cleaned for the app's text view: ${JSON.stringify({ rows: anns.rows.length, first: anns.rows[0].title, html: ann.html.slice(0, 60) })}`);
  const disc = await nc('discussions', { ctx });
  const open7003 = disc.sections.flatMap((x) => x.rows).find((r) => r.id === '7003');
  const tBefore = await nc('topic', { ctx, id: '7003' });
  const replied = await nc('reply', { ctx, id: '7003', text: 'First line\nsecond line\n\nNew paragraph' });
  const tAfter = await nc('topic', { ctx, id: '7003' });
  check(!disc.error && !!open7003 && tBefore.canReply && tBefore.entries.length > 0 && tBefore.entries.every((e) => 'depth' in e && e.text !== undefined) && replied.ok && tAfter.entries.length === tBefore.entries.length + 1 && tAfter.entries.some((e) => /First line<br>second line/.test(e.html)), `discussions in sections, a topic with its replies threaded by depth, and a reply from the phone posted as Canvas's paragraphs: ${JSON.stringify({ sections: disc.sections.map((x) => `${x.title}:${x.rows.length}`), before: tBefore.entries.length, after: tAfter.entries.length })}`);
  const fileOnly = tBefore.entries.find((e) => e.id === 'e3');
  check(!!fileOnly && fileOnly.text === '' && fileOnly.attachments?.length === 1 && /W8 Discussion Worksheet/.test(fileOnly.attachments[0].name) && /\/files\/att9\/download/.test(fileOnly.attachments[0].url) && tBefore.entries.filter((e) => e.id !== 'e3').every((e) => Array.isArray(e.attachments) && e.attachments.length === 0), `a reply that is a file and nothing else comes with its file: ${JSON.stringify(fileOnly)}`);
  check((await nc('reply', { ctx, id: '7003', text: '   ' })).error === 'Write a reply first.', 'an empty reply is refused with a reason');
  const mods = await nc('modules', { ctx });
  const markable = mods.modules.flatMap((m) => m.items.map((it) => ({ m, it }))).find((x) => x.it.markable);
  const marked = markable ? await nc('markDone', { ctx, module: markable.m.id, item: markable.it.id, done: true }) : null;
  const mods2 = await nc('modules', { ctx });
  const nowDone = mods2.modules.flatMap((m) => m.items).find((it) => it.id === markable?.it.id);
  check(!mods.error && mods.modules.length > 0 && mods.modules.every((m) => m.items.every((it) => it.header || it.url)) && !mods.modules.flatMap((m) => m.items).some((it) => /undefined/.test(it.url || '')) && !!markable && marked?.ok && nowDone?.done === true, `modules with every item's address and requirement; a "Mark done" item marked from the phone: ${JSON.stringify({ modules: mods.modules.map((m) => `${m.name}:${m.items.length}`), marked: markable?.it.title, done: nowDone?.done })}`);
  if (markable) await nc('markDone', { ctx, module: markable.m.id, item: markable.it.id, done: false });
  const asg = await nc('assignments', { ctx });
  const asgRows = asg.sections?.flatMap((x) => x.rows) || [];
  check(!asg.error && asg.sections.length > 0 && asg.sections.every((x) => ['Overdue', 'Upcoming', 'Undated', 'Past'].includes(x.title)) && asgRows.every((r) => /\/assignments\/\d+$/.test(r.url)) && (asg.sections.find((x) => x.title === 'Past')?.rows || []).every((r) => r.status?.word) && !asgRows.some((r) => r.status?.word === 'Not submitted') && asgRows.some((r) => r.status === null), `assignments by when they are due — a chip only for what is out of the ordinary (a score, Submitted, Missing…), never a plain "Not submitted" on work still to do: ${JSON.stringify(asg.sections.map((x) => `${x.title}:${x.rows.map((r) => r.status?.word || '-').join('|')}`))}`);
  // (Mac 1.3.8) work handed in as a file: the file carries Canvas's viewer for it (the teacher's marks), the work its page in Canvas
  let withFile = null;
  for (const cid of ['101', '102', '103', '104']) {
    const list = await nc('assignments', { ctx: `courses/${cid}` });
    for (const r of (list.sections || []).flatMap((x) => x.rows)) {
      if (!/Submitted|Graded|\d/.test(r.status?.word || '')) continue;
      const id = (r.url.match(/assignments\/(\d+)/) || [])[1];
      const one = id ? await nc('assignment', { course: cid, id }) : null;
      if (one?.submission?.files?.length && !one.quizId) { withFile = { cid, id, one }; break; }
    }
    if (withFile) break;
  }
  const wf = withFile?.one?.submission;
  check(!!wf && /\/mock-docviewer\//.test(wf.files[0].preview || '') && new RegExp(`/courses/${withFile.cid}/assignments/${withFile.id}/submissions/\\w+$`).test(wf.viewer || '') && (withFile.one.comments || []).every((c) => c.text === c.text.trim()), `a file handed in carries Canvas's viewer for it, the work its own page, comments trimmed: ${JSON.stringify({ at: withFile && `${withFile.cid}/${withFile.id}`, preview: wf?.files?.[0]?.preview, viewer: wf?.viewer })}`);
  // (Mac 1.3.10) a marked assignment's class numbers, for the box plot: the five numbers, the mean, your own score, out of what
  const boxA = await nc('assignment', { course: '101', id: '1008' });
  const bs = boxA.classStats;
  check(!!bs && [bs.min, bs.lowerQ, bs.median, bs.upperQ, bs.max, bs.mean, bs.possible].every((v) => typeof v === 'number') && bs.min <= bs.lowerQ && bs.lowerQ <= bs.median && bs.median <= bs.upperQ && bs.upperQ <= bs.max && typeof bs.mine === 'number', `a marked assignment carries the class's numbers for the box plot: ${JSON.stringify(bs)}`);
  // (Mac 1.3.8) a tool's assignment Canvas says cannot be submitted (can_submit: false, as of every tool's) is not called closed
  const toolA = await nc('assignment', { course: '104', id: '4003' });
  check(!toolA.error && !!toolA.toolUrl && toolA.why !== 'This assignment is closed.' && !toolA.canSubmit, `a tool's assignment open in the tool is not "closed": ${JSON.stringify({ why: toolA.why, tool: !!toolA.toolUrl, canSubmit: toolA.canSubmit })}`);
  // (Mac 1.3.7) Previous / Next: an assignment's neighbours in the course's own order, each a name and an assignment's address
  const around = await nc('neighbours', { ctx: 'courses/101', type: 'Assignment', id: '1012' });
  const pageAround = await nc('neighbours', { ctx: 'courses/101', type: 'Page', id: 'nope-no-such-page' });
  check(!around.error && (around.prev || around.next) && [around.prev, around.next].filter(Boolean).every((x) => x.name && /\/courses\/101\/assignments\/\d+/.test(x.url)) && !pageAround.error, `Previous / Next for the Mac: an assignment's neighbours, and nothing (not an error) for a page it cannot place: ${JSON.stringify({ around, pageAround })}`);
  const a12 = await nc('assignment', { course: '101', id: '1012' });
  check(!a12.error && a12.canSubmit && a12.types.includes('online_text_entry') && a12.types.includes('online_upload') && a12.status === null && /^\w{3} \d{1,2}(, \d{4})? at \d{1,2}:\d{2} [AP]M$/.test(a12.due) && a12.dueLine === `Due ${a12.due}` && a12.factsLine === `${a12.dueLine} · ${a12.points} · ${a12.attemptsText}` && a12.points, `an assignment for the app: its facts (one line of them too), its instructions, no chip while it is simply still to do, and the ways it can be handed in from the phone: ${JSON.stringify({ title: a12.title, types: a12.types, due: a12.due, facts: a12.factsLine, points: a12.points, status: a12.status })}`);
  const sentText = await nc('submit', { course: '101', id: '1012', type: 'online_text_entry', text: 'My answer\n\nwith two paragraphs', comment: 'Sent from the phone' });
  const a12b = await nc('assignment', { course: '101', id: '1012' });
  check(sentText.ok && /Submitted/.test(a12b.status?.word || '') && !!a12b.submitted && a12b.resubmit, `a text entry handed in from the phone (with a comment): ${JSON.stringify({ sentText, status: a12b.status, submitted: a12b.submitted })}`);
  const pdf = Buffer.from('%PDF-1.4\n% a tiny test file\n').toString('base64');
  const sentFile = await nc('submit', { course: '101', id: '1013', type: 'online_upload', files: [{ name: 'answer.pdf', type: 'application/pdf', data: pdf }] });
  check(sentFile.ok, `a file picked on the phone handed in through Canvas's upload: ${JSON.stringify(sentFile)}`);
  check((await nc('submit', { course: '101', id: '1013', type: 'online_upload', files: [] })).error === 'Choose a file to hand in.' && /web address/.test((await nc('submit', { course: '101', id: '1013', type: 'online_url', url: 'not a link' })).error || '') && /Canvas’s own page/.test((await nc('submit', { course: '101', id: '1013', type: 'media_recording' })).error || ''), 'a hand-in without a file, with a bad web address, or of a kind the phone cannot send is refused with a reason');
  const quizA = await nc('assignment', { course: '101', id: '1010' });
  check(!quizA.error && !quizA.canSubmit && !!quizA.quizUrl, `a quiz is taken on its own page, not handed in here: ${JSON.stringify({ quizUrl: quizA.quizUrl, canSubmit: quizA.canSubmit })}`);
  const graded = await nc('assignment', { course: '101', id: '1001' });
  const commented = await nc('commentOn', { course: '101', id: '1001', text: 'Thanks for the feedback' });
  const graded2 = await nc('assignment', { course: '101', id: '1001' });
  check(!graded.error && graded.grade?.text && graded.comments.length > 0 && commented.ok && graded2.comments.length === graded.comments.length + 1, `a graded assignment's grade and comments, and a comment added from the phone: ${JSON.stringify({ grade: graded.grade, comments: graded.comments.length, after: graded2.comments.length })}`);
  const pg = await nc('pages', { ctx });
  const front = await nc('page', { ctx, slug: '' });
  const one2 = await nc('page', { ctx, slug: pg.rows[0].slug });
  check(!pg.error && pg.rows.length > 0 && !front.error && front.title && /href="http:\/\/localhost:\d+\/courses\/101\//.test(front.html) && one2.title === pg.rows[0].title, `pages, the front page, a page by its name — every address in them made whole: ${JSON.stringify({ rows: pg.rows.map((r) => r.title), front: front.title })}`);
  const fl = await nc('files', { ctx });
  const sub1 = fl.folders[0] ? await nc('files', { ctx, folder: fl.folders[0].id }) : null;
  check(!fl.error && fl.folders.length > 0 && fl.files.length > 0 && fl.files.every((f) => f.url && f.name && f.sub) && sub1 && !sub1.error, `files: the course's folders and files with their sizes, and a folder opened: ${JSON.stringify({ folders: fl.folders.map((f) => f.name), files: fl.files.length })}`);
  const ppl = await nc('people', { ctx });
  check(!ppl.error && ppl.sections.length > 0 && ppl.sections[0].title.startsWith('Teacher') && ppl.sections.every((x) => x.rows.every((r) => r.name)), `people by role, teachers first: ${JSON.stringify(ppl.sections.map((x) => `${x.title}:${x.rows.length}`))}`);
  const qz = await nc('quizzes', { ctx });
  const sy = await nc('syllabus', { ctx });
  check(!qz.error && qz.rows.length > 0 && qz.rows.every((r) => r.url) && !sy.error && sy.html && sy.rows.length > 0, `quizzes with their status, the syllabus with its dated work: ${JSON.stringify({ quizzes: qz.rows.length, syllabus: sy.html.slice(0, 40), rows: sy.rows.length })}`);
  const cg = await nc('courseGrades', { id: '101', fresh: true });
  const weightedIds = new Set(cg.groups.filter((g) => /^[1-9]\d*% of grade/.test(g.weightText)).map((g) => g.id));
  const ungradedRow = cg.rows.find((r) => r.effective === null && r.possible > 0 && r.counted && weightedIds.has(r.groupId));
  const cgLow = await nc('courseGrades', { id: '101', tried: { [ungradedRow.id]: 0 } });
  const cgHigh = await nc('courseGrades', { id: '101', tried: { [ungradedRow.id]: ungradedRow.possible } });
  const cgBack = await nc('courseGrades', { id: '101', tried: {} });
  check(!cg.error && cg.total === 92.4 && cg.letter && cg.groups.length > 0 && cg.rows.length > 0 && cg.scale.length === 11 && !cg.whatIf && cgLow.whatIf && cgHigh.whatIf && cgLow.total < cgHigh.total && cgLow.rows.find((r) => r.id === ungradedRow.id)?.hypothetical && cgLow.rows.find((r) => r.id === ungradedRow.id)?.scoreText === `0/${ungradedRow.possible}` && Number.isFinite(cgLow.gpaIf) && cgLow.gpaIf <= cgHigh.gpaIf && cgBack.total === cg.total && !cgBack.whatIf, `a course's grades for the app's sheet; a what-if zero and a what-if full mark move the total, the letter and the term GPA apart, and clearing them brings Canvas's total back (nothing saved): ${JSON.stringify({ total: cg.total, letter: cg.letter, groups: cg.groups.map((g) => `${g.name} ${g.weightText} ${g.value}`), row: ungradedRow?.name, zero: [cgLow.total, cgLow.letter, cgLow.gpaIf], full: [cgHigh.total, cgHigh.letter, cgHigh.gpaIf], back: cgBack.total })}`);
  // (Mac 1.3.69) the breakdown bar's data: each weighted group's share (summing to 100), graded or not; none by points
  const bar = cg.groups.filter((g) => g.weight > 0);
  const cgPts = await nc('courseGrades', { id: '102', fresh: true });
  check(cg.weighted && bar.length > 0 && Math.abs(bar.reduce((s, g) => s + g.weight, 0) - 100) < 0.5 && bar.every((g) => typeof g.graded === 'boolean') && !cgPts.weighted && cgPts.groups.every((g) => g.weight === null), `a weighted course's groups carry their share of the grade for the breakdown bar; a course by points carries none: ${JSON.stringify({ weighted: bar.map((g) => `${g.name} ${g.weight} ${g.graded}`), points: cgPts.groups.map((g) => g.weight) })}`);
  // ---- (2.99.33) the UI review's fixes in the engine: every app shows the same words, figures and dates, made here once ----
  console.log('the engine says it once, for every app (2.99.33)');
  // the counters say what they count, and an empty one says so in words
  const t34 = await nc('today');
  const lab = Object.fromEntries(t34.counters.map((c) => [c.key, c]));
  check(lab.unread.fullLabel === 'Announcements' && /^\d+ unread · (\d+ courses?|.+)$|^All caught up$/.test(lab.unread.fullNote) && lab.graded.fullLabel === 'Graded this week' && lab.today.fullLabel === 'Due today' && lab.tomorrow.fullLabel === 'Tomorrow' && t34.counters.every((c) => !/^0 /.test(c.note || '') && !/\b0 points\b/.test(c.note || '')) && t34.load.every((l) => l.text === `${l.done} / ${l.total} handed in`) && t34.loadCaption === 'Handed in this week' && t34.list.rows.every((r) => 'kind' in r && 'pts' in r), `the counters' full names say what they count (Announcements "3 unread · 2 courses", Graded this week), no "0 points total", and the week's load in words: ${JSON.stringify({ counters: t34.counters.map((c) => `${c.fullLabel}: ${c.fullNote || ''}`), load: t34.load.map((l) => l.text) })}`);
  // the name every tile fits (what an app that reads only `label` shows: the iPhone's three across, a row of six on a
  // narrow window) is the short one, and its line says the rest — "Unread" with "announcements · 2 courses", "Graded"
  // with "this week · …" — never a full name cut short
  const tc34 = await nc('todayCounts', { kept: false });
  check(t34.counters.every((c) => c.fullLabel && c.label.length <= c.fullLabel.length) && lab.unread.label === 'Unread' && /^(announcements? · .+|All caught up)$/.test(lab.unread.note) && lab.graded.label === 'Graded' && lab.next.label === 'Next 7 days' && lab.today.fullNote === lab.today.note && /^(this week · .+ points?|No grades posted this week)$/.test(tc34.gradedNote) && (tc34.gradedNote === 'No grades posted this week' ? tc34.gradedFullNote === tc34.gradedNote : `this week · ${tc34.gradedFullNote}` === tc34.gradedNote), `each tile's name that always fits and the line that says the rest ("Unread" · "announcements · 2 courses", "Graded" · "this week · …"), and the full ones beside: ${JSON.stringify({ unread: [lab.unread.label, lab.unread.note, lab.unread.fullLabel, lab.unread.fullNote], graded: [lab.graded.label, tc34.gradedNote, lab.graded.fullLabel, tc34.gradedFullNote] })}`);
  // the Announcements sheet: each row names its course (the sidebar's name, in its colour) and the day, never "unread" again
  const un34 = await nc('todaySheet', { key: 'unread' });
  const unRows = un34.sections.flatMap((x) => x.rows);
  const sidebarNames = new Set((await nc('courses')).rows.map((r) => r.code));
  check(un34.title === 'Announcements' && /unread/.test(un34.note) && unRows.length > 0 && unRows.every((r) => !/unread|Posted/.test(r.sub) && sidebarNames.has(r.course) && /^\w{3} \d{1,2}(, \d{4})?$/.test(r.when) && r.sub === `${r.course} · ${r.when}`) && new Set(unRows.map((r) => r.color)).size > 1, `the Announcements sheet: "F26-PHYS 008 01 · Oct 12" under each title, in its course's colour: ${JSON.stringify({ note: un34.note, rows: unRows.map((r) => `${r.sub} ${r.color}`) })}`);
  // a counter's rows carry their facts one by one for the apps' own columns (the due time on the right, no "Assignment ·" in front)
  const nx34 = (await nc('todaySheet', { key: 'next' })).sections.flatMap((x) => x.rows);
  check(nx34.length > 0 && nx34.every((r) => r.course && r.kind && /\d{1,2}:\d{2} [AP]M$/.test(r.when) && !/^(Assignment|Quiz|Discussion) · /.test(r.sub.slice(r.course.length + 3))), `the next-7-days sheet's rows: course, kind, detail and the due time apart: ${JSON.stringify(nx34.slice(0, 3).map((r) => [r.course, r.kind, r.detail, r.when, r.sub]))}`);
  // the term GPA rounded once, half up (the mock's 3.425 is 3.43 on every app), and the goal's gap from that figure
  const gr34 = await nc('grades');
  const half = (v, p) => Math.floor(Math.abs(v) * 10 ** p + 0.5 + 1e-9 * Math.max(1, Math.abs(v) * 10 ** p)) / 10 ** p;
  check(Math.abs(gr34.gpa - 3.425) < 1e-9 && gr34.gpaText === '3.43' && gr34.gpaShown === 3.43 && gr34.goalText === '4.00' && gr34.goalLine === `${(gr34.goal - 3.43).toFixed(2)} below goal` && gr34.goalGap === -half(gr34.goal - 3.43, 2), `the term GPA rounded once, half up — 3.425 is 3.43 — and the goal's gap worked out from 3.43, so the two add up: ${JSON.stringify({ gpa: gr34.gpa, text: gr34.gpaText, goal: gr34.goalText, line: gr34.goalLine })}`);
  // a course with a hidden total says why, and the GPA's note names it; no bare N/A anywhere
  const hid = gr34.rows.find((r) => r.id === '103');
  check(hid && hid.pctText === '–' && hid.why === 'Hidden by instructor' && hid.hidden === true && /hidden by the instructor/.test(gr34.gpaNote) && /^From 4 courses, each counting equally\./.test(gr34.gpaNote) && !gr34.rows.some((r) => r.pctText === 'N/A') && gr34.rows.filter((r) => r.pct !== null).every((r) => r.pctText === `${half(r.pct, 1).toFixed(1).replace(/\.0$/, '')}%`), `a hidden total: '–' and "Hidden by instructor", and the GPA's note says what it leaves out: ${JSON.stringify({ row: hid && [hid.pctText, hid.why], note: gr34.gpaNote })}`);
  // one graded count for the overview and the course's own page; What-If starts from the LMS's own total
  const m101 = gr34.rows.find((r) => r.id === '101');
  const cgOn = await nc('courseGrades', { id: '101', on: true });
  const cgOff = await nc('courseGrades', { id: '101' });
  check(m101.gradedText === cgOff.gradedText && m101.graded === cgOff.graded && m101.total === cgOff.totalItems && /^\d+ of \d+ graded/.test(m101.gradedText) && cgOn.whatIf && cgOn.total === cgOff.total && cgOn.total === 92.4 && cgOn.letter === 'A−' && cgOn.totalText === '92.4%' && /Simpl’s own estimate differs from Canvas’s total by [\d.]+ points\./.test(cgOn.estimateNote) && cgOff.note === 'Canvas’s total, weighted across the groups with graded work.' && !/A-/.test(cgOff.note) && cgOff.gpaText === gr34.gpaText, `one graded count on the Grades overview and the course's page (${m101.gradedText}); What-If turned on with nothing typed keeps Canvas's 92.4% A− (it jumped to Simpl's own reading), and says how far Simpl's own reading is: ${JSON.stringify({ overview: m101.gradedText, page: cgOff.gradedText, on: [cgOn.total, cgOn.letter, cgOn.estimateNote], note: cgOff.note })}`);
  // a score tried moves the total from Canvas's, by what it changes
  const wRow = cgOff.rows.find((r) => r.effective === null && r.possible > 0 && r.counted && weightedIds.has(r.groupId));
  const cgTry = await nc('courseGrades', { id: '101', tried: { [wRow.id]: 0 } });
  check(cgTry.whatIf && cgTry.total !== null && cgTry.total < 92.4 && /moved by the scores you tried/.test(cgTry.note), `a zero tried moves What-If's total down from Canvas's 92.4%: ${JSON.stringify({ tried: wRow.name, total: cgTry.total, note: cgTry.note })}`);
  // the grade as one figure: 81.25 is 81.3%, the ring's whole percent, its band, one chip; the rubric's mark is the points given
  const rub = await nc('assignment', { course: '104', id: '4001' });
  const corr = rub.rubric.find((r) => r.name === 'Correctness');
  const work = rub.rubric.find((r) => r.name === 'Work shown');
  check(rub.status?.word === 'Graded' && rub.grade.pctText === '100%' && rub.grade.pctWhole === 100 && rub.grade.band === 'A' && rub.grade.chip === `${rub.grade.text} · ${rub.grade.letter}` && corr.adjusted === true && corr.score === 4 && corr.markText === 'Your mark · 4 pts' && corr.ratings.find((x) => x.got)?.value === 3 && work.adjusted === false && work.markText === 'Your mark', `a graded assignment: the chip says Graded (the grade card has the score), the grade in one chip; a criterion marked Partial (3) but given 4 says "Your mark · 4 pts": ${JSON.stringify({ status: rub.status, chip: rub.grade.chip, corr: [corr.score, corr.adjusted, corr.markText] })}`);
  const late34 = await nc('assignment', { course: '101', id: '1008' });
  check(late34.status?.word === 'Late' && late34.grade.band === 'F' && late34.grade.pctText === '0%' && /^Submitted \w{3} \d{1,2} at \d{1,2}:\d{2} [AP]M · late$/.test(late34.submitted), `work graded late: the chip says Late, the grade's band F (for its colour), the hand-in time written as every other: ${JSON.stringify({ status: late34.status, band: late34.grade.band, submitted: late34.submitted })}`);
  // times: "11:59 PM" in every answer, never "11:59pm"; dates with the year only when it is not this year
  const lower = (o) => JSON.stringify(o).match(/\b\d{1,2}:\d{2}(am|pm)\b/gi)?.filter((x) => /[ap]m$/.test(x)) || [];
  const timed = [await nc('assignments', { ctx: 'courses/101' }), await nc('notifications'), await nc('home', { ctx: 'courses/101' }), await nc('modules', { ctx: 'courses/101' }), await nc('quizzes', { ctx: 'courses/101' }), await nc('discussions', { ctx: 'courses/101' }), await nc('externalTools'), late34, rub];
  check(timed.every((x) => !x.error) && timed.every((x) => lower(x).length === 0), `every time in the answers is "11:59 PM", never "11:59pm": ${JSON.stringify(timed.flatMap(lower).slice(0, 5))}`);
  // notifications: each fact once
  const nf34 = (await nc('notifications')).days.flatMap((d) => d.rows);
  // (the overdue one was dismissed above: Due soon's are written the same way)
  const soon34 = nf34.filter((r) => r.cat === 'soon' || r.cat === 'overdue');
  check(soon34.length > 0 && soon34.every((r) => (r.cat === 'overdue' ? /^[^·]+ · (Missing|Not handed in)( · \d+ pts)? · was due \w{3} \d{1,2} at \d{1,2}:\d{2} [AP]M$/ : /^[^·]+ · \w+( · \d+ pts)? · Due \w{3} \d{1,2} at \d{1,2}:\d{2} [AP]M$/).test(r.sub)) && !nf34.some((r) => /hours past|days past|no submission|not started/.test(r.sub)), `a notification's line says each fact once: ${JSON.stringify(soon34.map((r) => r.sub))}`);
  // a quiz's attempts said once, counting the one under way; the access code's rule not said twice
  const qi34 = await nc('quizIntro', { course: '101', quiz: '9011' });
  check(qi34.note === 'This is your only attempt' && !qi34.rules.some((r) => r.symbol === 'bolt.fill' || r.symbol === 'key.fill') && qi34.attemptsText === '0 of 1 attempt used' && qi34.facts[2] === `Due ${qi34.facts[2].slice(4)}` && /at \d{1,2}:\d{2} [AP]M$/.test(qi34.facts[2]) && qi34.factsLine === `${qi34.facts[2]} · ${qi34.facts[1].replace(/ points?$/, (w) => (w === ' point' ? ' pt' : ' pts'))} · ${qi34.attemptsText}`, `a quiz's intro says its attempts once ("This is your only attempt"), in the assignment's words for its facts, and the one-column facts line with the quiz's own attempts: ${JSON.stringify({ note: qi34.note, rules: qi34.rules.map((r) => r.symbol), attempts: qi34.attemptsText, facts: qi34.facts, line: qi34.factsLine })}`);
  // one course name: the sidebar's, and a second line only for something new
  const dc34 = await nc('dashCourses');
  const ac34 = await nc('allCourses');
  const home34 = await nc('home', { ctx: 'courses/101' });
  const setup34 = await nc('setupInfo');
  check(dc34.rows.find((r) => r.id === '101')?.altName === '' && dc34.rows.find((r) => r.id === '103')?.altName === '' && ac34.current.find((r) => r.code.startsWith('Academic Success'))?.altName === 'ASRS' && dc34.rows.every((r) => r.unreadText === (r.unread ? `${r.unread} new` : '')) && dc34.rows.find((r) => r.id === '103')?.scoreText === '–' && home34.sub === 'Fall 2026' && gr34.rows.find((r) => r.id === '101')?.altName === '' && ac34.current.find((r) => r.id === '101')?.altName === '' && setup34.courses.find((c) => c.id === '101')?.sub === 'Fall 2026 · Yue Lei' && setup34.courses.find((c) => c.id === '101')?.label === 'F26-MATH 021 20' && cgOff.altName === '' && ac34.subs.past === '2 past courses' && ac34.subs.future === 'None yet' && ac34.sub === ac34.subs.current, `a course named once ("F26-MATH 021 20", never again as "MATH-021-20"; nor "PHYS-008HL" under "F26-PHYS 008HL 01/PHYS 008L 01", which says it already — "ASRS" under its long name does say more), a card's badge "1 new", the course home's line its term alone, and Courses' line for each filter: ${JSON.stringify({ card: dc34.rows.find((r) => r.id === '101'), home: home34.sub, subs: ac34.subs })}`);
  // a course's score as every card, row and bar shows it: "92.4%" or "–", and whole half up for a figure of whole percents
  const sky34 = await nc('dashSkyline');
  const wholeOk = (r) => (r.score === null ? r.scoreText === '–' && r.scoreWhole === null : r.scoreText === `${half(r.score, 1).toFixed(1).replace(/\.0$/, '')}%` && r.scoreWhole === half(half(r.score, 1), 0));
  check(sky34.courses?.length > 0 && sky34.courses.every(wholeOk) && dc34.rows.every(wholeOk) && ac34.current.every(wholeOk) && dc34.rows.find((r) => r.id === '101')?.scoreWhole === 92, `a course's score as text and as a whole percent, half up, on the cards, the lists and the Dashboard's bars: ${JSON.stringify(sky34.courses?.map((c) => [c.code, c.scoreText, c.scoreWhole]))}`);
  // search: the same words, the same rows in the same order, each course by the sidebar's name
  const srch1 = await nc('search', { q: 'lab' });
  const srch2 = await nc('search', { q: 'lab' });
  const sMath = await nc('search', { q: 'math' });
  const flat = (x) => x.groups.map((g) => `${g.title}:${g.rows.map((r) => r.url).join(',')}`).join(' | ');
  check(flat(srch1) === flat(srch2) && sMath.groups.find((g) => g.title === 'Courses')?.rows[0]?.title === 'F26-MATH 021 20' && srch1.groups.flatMap((g) => g.rows).every((r) => !/MATH-021-20|PHYS-008-01/.test(r.sub)), `search answers in one order, run after run, each course by the sidebar's name: ${JSON.stringify(srch1.groups.map((g) => `${g.title}:${g.rows.map((r) => r.title).join('|')}`))}`);
  // a file to a message: uploaded, named on the message, and there when the conversation is read
  const up34 = await nc('messageUpload', { name: 'notes.txt', type: 'text/plain', data: Buffer.from('Lab notes').toString('base64') });
  const sent34 = await nc('sendMessage', { recipients: ['12'], subject: 'With a file', body: 'See the file', attachments: [up34.id] });
  const inb34 = await nc('inbox', { scope: 'inbox' });
  const conv34 = await nc('conversation', { id: inb34.rows.find((r) => r.subject === 'With a file')?.id });
  check(!up34.error && up34.id && up34.name === 'notes.txt' && sent34.ok && conv34.messages?.[0]?.attachments?.some((x) => x.name === 'notes.txt'), `a file sent with a new message (planning-18): uploaded to the student's files, then named on the message: ${JSON.stringify({ up: up34, sent: sent34, files: conv34.messages?.[0]?.attachments })}`);
  // a group's course by its name (its code for a tooltip); What's New's dates as the app writes them; To Do's figures
  const grp34 = await nc('groups');
  const wn34 = await nc('whatsNew');
  const td34 = await nc('todo', { group: 'date' });
  const files34 = await nc('files', { ctx: 'courses/101' });
  check(grp34.current[0]?.course === 'Academic Success Resource Site (2026-27)' && grp34.current[0]?.courseCode === 'ASRS' && wn34.releases.every((r) => /^\w{3} \d{1,2}(, \d{4})?$/.test(r.dateText)) && td34.left + td34.done === td34.total && td34.leftText === `${td34.left} left` && Number.isInteger(td34.overdue) && files34.files.every((f) => ['PDF', 'Image', 'Spreadsheet', 'Text', 'Document', 'Slides', 'Video', 'Audio', 'Archive', 'Web page', 'File'].includes(f.typeText)), `a group's course by its name, What's New dated "Oct 12", To Do's "17 left", a file's type in one vocabulary: ${JSON.stringify({ group: grp34.current[0], dates: wn34.releases.map((r) => r.dateText), todo: [td34.leftText, td34.overdueText], types: files34.files.map((f) => f.typeText) })}`);
  // every figure half up on its decimal value, whatever the binary double holds — the store's own (a group's percent, a
  // score's points) as the answers' — and a negative one the same way from zero
  const rounded = await sp.evaluate(() => { const S = self.BCV.store; return { r: [[3.425, 2], [81.25, 1], [2.5, 0], [-2.5, 0], [1.005, 2], [72.5, 0], [(87 / 120) * 100, 0], [4.35, 1], [-0.575, 2]].map(([v, p]) => S.roundHalfUp(v, p)), pts: [4.35, 0.15, 12, 2.25].map(S.fmtPts) }; });
  check(rounded.r.join() === '3.43,81.3,3,-3,1.01,73,73,4.4,-0.58' && rounded.pts.join() === '4.4,0.2,12,2.3', `numbers round half up on their decimal value (3.425 → 3.43, 81.25 → 81.3, 2.5 → 3, −2.5 → −3, 87 of 120 → 73%; points 4.35 → 4.4): ${JSON.stringify(rounded)}`);
  // dates as every other is written: the year only when it is not this year (a page edited, a message), a sheet's day
  // with its weekday ("Tue, Oct 13"), work due tomorrow under "Tomorrow"; an empty total '–' like every empty score
  const pg34 = await nc('pages', { ctx: 'courses/101' });
  const ib34 = await nc('inbox', { scope: 'inbox' });
  const tdy34 = await nc('todaySheet', { key: 'today' });
  const nfd34 = (await nc('notifications')).days.map((d) => d.title);
  const cg106 = await nc('courseGrades', { id: '106', fresh: true });
  const thisYear = String(new Date().getFullYear());
  const tomorrowDate = new Date(Date.now() + 864e5).toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
  check(pg34.rows.some((r) => /Edited/.test(r.sub)) && !pg34.rows.some((r) => r.sub.includes(thisYear)) && ib34.rows.every((r) => /^(\d{1,2}:\d{2} [AP]M|\w{3} \d{1,2}(, \d{4})?)$/.test(r.when)) && /· \w{3}, \w{3} \d{1,2}$/.test(tdy34.note) && !nfd34.includes(tomorrowDate) && (cg106.total !== null || cg106.totalText === '–'), `a date carries its year only when it is not this year, a sheet's day reads "Tue, Oct 13", tomorrow's work sits under Tomorrow, a course with nothing graded totals '–': ${JSON.stringify({ pages: pg34.rows.map((r) => r.sub), inbox: ib34.rows.map((r) => r.when), note: tdy34.note, days: nfd34, empty: [cg106.total, cg106.totalText] })}`);
  // (2.99.33) what a figure is worked out from is the figure shown: a ring's whole percent and a grade's band and letter
  // from the one-decimal percent beside them (89.96 is shown "90%", its ring 90 and its letter an A−); an empty score "–"
  const graded34 = [];
  for (const id of ['1001', '1008', '1010', '1011', '1012']) { const a = await nc('assignment', { course: '101', id }); if (a.grade) graded34.push(a.grade); }
  const cgRows34 = (await nc('courseGrades', { id: '101', fresh: true })).rows;
  check(graded34.length > 0 && graded34.every((g) => g.pct === null || (g.pctWhole === half(half(g.pct, 1), 0) && g.pctText === `${half(g.pct, 1).toFixed(1).replace(/\.0$/, '')}%` && (!/^[A-F][+−]?$/.test(g.letter || '') || g.band === g.letter[0]))) && cgRows34.every((r) => /^(–|\d+(\.\d)?)\/\d+(\.\d)?$/.test(r.scoreText)), `a grade's ring, band and letter agree with its percent as shown; an empty row score "–/30": ${JSON.stringify(graded34.map((g) => [g.pctText, g.pctWhole, g.letter, g.band]))}`);
  // the Dashboard's Coming up days: each day's date as every date is written, saying only what its title does not
  const dl34 = await nc('dashList');
  const WEEKDAY = /^(Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday)$/;
  check(dl34.days.length > 0 && dl34.days.every((d) => (['Today', 'Tomorrow', 'Yesterday'].includes(d.title) ? /^\w{3}, \w{3} \d{1,2}(, \d{4})?$/ : WEEKDAY.test(d.title) ? /^\w{3} \d{1,2}(, \d{4})?$/ : WEEKDAY).test(d.date)), `Coming up's days: "Today" over "Tue, Oct 13", "Thursday" over "Oct 15", never "Tuesday, October 13": ${JSON.stringify(dl34.days.map((d) => [d.title, d.date]))}`);
  // Canvas's own words about a lock carry its times as every time is written ("12am" is "12:00 AM")
  await sp.route('**/api/v1/courses/101/pages/locked-notes*', (route) => route.fulfill({ contentType: 'application/json', body: JSON.stringify({ title: 'Locked notes', url: 'locked-notes', body: '<p>Notes</p>', locked_for_user: true, lock_explanation: 'This page is locked until Oct 20 at 12am. The unit closes Oct 19 at 11:59pm.' }) }));
  const lockedPg34 = await nc('page', { ctx: 'courses/101', slug: 'locked-notes' });
  await sp.unroute('**/api/v1/courses/101/pages/locked-notes*');
  check(lockedPg34.lockText === 'This page is locked until Oct 20 at 12:00 AM. The unit closes Oct 19 at 11:59 PM.' && lockedPg34.html === '', `a lock's reason in Canvas's words, its times written as every other: ${JSON.stringify(lockedPg34.lockText)}`);
  // (1.3) a quiz taken in the app's own screens: every Classic kind, saved, flagged, handed in, its feedback read
  await sp.evaluate(() => fetch('/__mock/config', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ richQuestions: true, moreTypes: true }) }));
  const qi = await nc('quizIntro', { course: '101', quiz: '9011' });
  check(!qi.error && qi.title === 'Lec06-PreQuiz' && qi.canStart && /Begin/.test(qi.begin) && qi.rules.some((r) => r.symbol === 'timer') && qi.facts.length === 3 && qi.html, `a quiz's intro for the app: its facts, its rules as lines (the time limit among them), and Begin: ${JSON.stringify({ title: qi.title, begin: qi.begin, facts: qi.facts, rules: qi.rules?.map((r) => r.symbol), error: qi.error })}`);
  const qb = await nc('quizBegin', { course: '101', quiz: '9011' });
  const qa = qb.attempt || {};
  const qk = (k) => (qa.questions || []).find((q) => q.kind === k);
  const kinds = (qa.questions || []).map((q) => q.kind);
  check(!qb.error && ['choice', 'multi', 'number', 'match', 'drops', 'essay', 'file'].every((k) => kinds.includes(k)) && qa.timed && !!qa.endAt && qa.questions.every((q) => q.html && q.id && q.n) && qk('match').matches.length >= 3 && qk('drops').blanks.length === 2 && qk('drops').blanks.every((b) => b.options.length >= 2) && /<span style="[^"]*">1<\/span>/.test(qk('drops').html) && qk('choice').options.length >= 2 && qa.questions.some((q) => q.points !== null), `an attempt begun from the app: every kind, a match's list, a blank's own options and its numbered mark in the words, the clock's end, each question's points: ${JSON.stringify({ kinds, endAt: qa.endAt, error: qb.error, drops: qk('drops')?.html })}`);
  const mc = qk('choice'), ma = qk('multi'), nu = qk('number'), mt = qk('match'), dr = qk('drops'), es = qk('essay');
  // (1.4) the review's one line per question reads a formula picture as maths, never as its LaTeX ("\\vec{v}")
  check(!/\\/.test(ma.plain) && /vector quantities, such as v\u20d7\?/.test(ma.plain), `the review's line reads a formula as maths, not LaTeX commands — a vector keeps its arrow (v⃗, never a bare v): ${JSON.stringify(ma.plain)}`);
  // (2.99.33) each blank's menu carries the words around it, so the choice reads beside its sentence ("the ___ of")
  check(dr.blanks.length === 2 && dr.blanks.every((b) => /___/.test(b.around || '')) && /\bthe ___ of\b/.test(dr.blanks[0].around), `a blank carries the words either side of it for its menu's label: ${JSON.stringify(dr.blanks.map((b) => b.around))}`);
  const saves = [
    await nc('quizAnswer', { course: '101', quiz: '9011', question: mc.id, value: { pick: mc.options[0].id } }),
    await nc('quizAnswer', { course: '101', quiz: '9011', question: ma.id, value: { picks: [ma.options[0].id, ma.options[2].id] } }),
    await nc('quizAnswer', { course: '101', quiz: '9011', question: nu.id, value: { text: '3,15' } }),
    await nc('quizAnswer', { course: '101', quiz: '9011', question: mt.id, value: { map: Object.fromEntries(mt.options.map((o, i) => [o.id, mt.matches[i % mt.matches.length].id])) } }),
    await nc('quizAnswer', { course: '101', quiz: '9011', question: dr.id, value: { map: Object.fromEntries(dr.blanks.map((b) => [b.id, b.options[0].id])) } }),
    await nc('quizAnswer', { course: '101', quiz: '9011', question: es.id, value: { text: 'Pros:\n\n• together\n• faster' } }),
  ];
  const qf = await nc('quizFlag', { course: '101', quiz: '9011', question: nu.id, on: true });
  const again = await nc('quizAttempt', { course: '101', quiz: '9011' });
  const byId = (id) => again.questions.find((q) => q.id === id);
  check(saves.every((x) => x.ok && x.done) && qf.ok && byId(mc.id).pick === mc.options[0].id && byId(ma.id).picks.length === 2 && byId(nu.id).text === '3.15' && byId(nu.id).flagged && Object.keys(byId(mt.id).map).length === mt.options.length && Object.keys(byId(dr.id).map).length === 2 && /together/.test(byId(es.id).text) && !/</.test(byId(es.id).text), `answers of every kind saved from the app (a comma decimal as a number, a match's pairs, a blank each, an essay as Canvas's HTML shown back as words), a flag, and the attempt read again as it stands: ${JSON.stringify({ saves, qf, essay: byId(es.id)?.text })}`);
  const qs1 = await nc('quizSubmit', { course: '101', quiz: '9011' });
  check(!qs1.error && qs1.ok && /Submitted/.test(qs1.title) && /of/.test(qs1.answered), `the attempt handed in from the app, its receipt: ${JSON.stringify(qs1)}`);
  const fb = await nc('quizFeedback', { course: '101', quiz: '9011' });
  check(!fb.error && (fb.hidden || (fb.rows.length >= 6 && fb.rows.every((r) => r.verdict && r.html) && fb.rows.some((r) => r.options?.some((o) => o.mine)) && fb.score !== undefined)), `its feedback for the app, question by question (or why it is held back): ${JSON.stringify({ hidden: fb.hidden, rows: fb.rows?.map((r) => `${r.n}:${r.verdict}:${r.score}`), score: fb.score, error: fb.error })}`);
  // (Mac 1.3.32) the quiz's intro lists each attempt with its score; a quiz's card reads its own assignment for its grade
  const qi2 = await nc('quizIntro', { course: '101', quiz: '9011' });
  const at1 = (qi2.attempts || [])[0];
  check(!qi2.error && at1 && at1.attempt >= 1 && (at1.score === null ? !!at1.why : /^[\d.]+ \/ [\d.]+$/.test(at1.score)), `a quiz's intro lists its attempts, each with its score (or why it is not shown): ${JSON.stringify({ attempts: qi2.attempts, keptNote: qi2.keptNote, error: qi2.error })}`);
  const qasg = await nc('quizAssignment', { course: '101', quiz: '9011' });
  const qown = await nc('assignment', { course: '101', id: '1011' });
  // ((2.99.33) its chip, as the assignment's own: none for work simply still to do, never a plain "Not submitted")
  check(!qasg.error && qasg.title === 'Lec06-PreQuiz' && qasg.id === '1011' && 'status' in qasg && JSON.stringify(qasg.status) === JSON.stringify(qown.status) && JSON.stringify(qasg.grade) === JSON.stringify(qown.grade) && qasg.status?.word !== 'Not submitted', `a quiz's card reads the quiz's own assignment (its grade and when it was handed in): ${JSON.stringify({ title: qasg.title, id: qasg.id, status: qasg.status, grade: qasg.grade, error: qasg.error })}`);
  const qp = await nc('quizBegin', { course: '101', quiz: '9014' }); // (Lec07: one question at a time, no going back — read from Canvas's own take page)
  const qpa = qp.attempt || {};
  const qn = qpa.questions?.length ? await nc('quizGo', { course: '101', quiz: '9014', move: 'next' }) : null;
  check(!qp.error && qpa.paged && qpa.noBack && qpa.questions.length >= 4 && qpa.questions[qpa.idx]?.loaded && qn && !qn.error && qn.idx === qpa.idx + 1 && qn.questions[qn.idx].loaded, `a one-at-a-time quiz in the app: its questions from Canvas's take page, and Next moves through that page: ${JSON.stringify({ paged: qpa.paged, n: qpa.questions?.length, idx: qpa.idx, next: qn?.idx, error: qp.error || qn?.error })}`);
  const coded = await nc('quizBegin', { course: '102', quiz: '10019' });
  check(coded.needsCode && /access code/i.test(coded.refused || ''), `a quiz with an access code asks for it before it begins (no attempt opened): ${JSON.stringify(coded)}`);
  await sp.evaluate(() => fetch('/__mock/config', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ richQuestions: false, moreTypes: false }) }));
  // (1.3) the iPhone's own setup: the courses that count and the goals, read and written (nothing of the look)
  const su = await nc('setupInfo');
  const onIds = (su.courses || []).filter((c) => c.on).map((c) => c.id);
  const first = onIds[0];
  const saved1 = await nc('setupSave', { courses: onIds, nicknames: {}, targets: { [first]: 'B+' }, tracking: true, goal: 3.7 });
  const su2 = await nc('setupInfo');
  const snapSetup = await nc('snapshot');
  const saved2 = await nc('setupSave', { courses: onIds, nicknames: {}, targets: { [first]: su.courses.find((c) => c.id === first)?.target || 'A+' }, tracking: su.tracking, goal: su.goal });
  check(!su.error && su.done === true && su.courses.length > 0 && onIds.length > 0 && su.grades.join() === 'C,B,B+,A−,A,A+' && saved1.ok && saved1.courses === onIds.length && su2.courses.find((c) => c.id === first)?.target === 'B+' && su2.goal === 3.7 && su2.courses.filter((c) => c.on).length === onIds.length && snapSetup.setupDone === true && saved2.ok, `the iPhone's setup: the current courses (the chosen ones ticked), the targets and the goal written and read back, the courses left as chosen, the snapshot says it is done: ${JSON.stringify({ courses: su.courses?.length, on: onIds.length, target: su2.courses?.find((c) => c.id === first)?.target, goal: su2.goal, setupDone: snapSetup.setupDone, error: su.error || saved1.error })}`);
  // (2.99.33) the targets with the real minus ("A−"), as every screen writes a letter; one sent back either way is kept as
  // "A-", the spelling the Grades screen's own target keeps (setTarget)
  const savedMinus = await nc('setupSave', { courses: onIds, nicknames: {}, targets: { [first]: 'A−' }, tracking: su.tracking, goal: su.goal });
  const suMinus = await nc('setupInfo');
  const keptMinus = await sp.evaluate((id) => self.BCV.store.pref('gradeTargets').then((t) => t?.[id] ?? null), first);
  await nc('setupSave', { courses: onIds, nicknames: {}, targets: { [first]: su.courses.find((c) => c.id === first)?.target || 'A+' }, tracking: su.tracking, goal: su.goal });
  check(savedMinus.ok && suMinus.courses.find((c) => c.id === first)?.target === 'A−' && keptMinus === 'A-', `a setup target sent back as "A−" is kept as "A-" and read back as "A−": ${JSON.stringify({ read: suMinus.courses?.find((c) => c.id === first)?.target, kept: keptMinus })}`);
  // (1.3) What's New after an update is marked seen when the app's sheet is really up, not when it is asked for:
  // a sheet held back (a sign-in still under way) comes up later, still due
  await sp.evaluate(() => self.BCV.api.storage.local.set({ 'whatsnew:seen': '2.99.2' }));
  const peek1 = await nc('whatsNew', { due: true, peek: true });
  const peek2 = await nc('whatsNew', { due: true, peek: true });
  const seenCall = await nc('whatsNewSeen', { version: peek2.version });
  const after = await nc('whatsNew', { due: true, peek: true });
  const webNotes = await sp.evaluate(() => !!document.querySelector('.bcv-wn, #bcv-whatsnew, .bcv-whatsnew'));
  check(peek1.releases?.length > 0 && peek2.releases?.length > 0 && peek2.version === manifest.version && seenCall.ok && after.releases?.length === 0 && !webNotes, `What's New for the app's sheet: still due however often it is asked for, seen once the sheet says it is up, and never the page's own notes under the app's screens: ${JSON.stringify({ first: peek1.releases?.map((r) => r.version), second: peek2.releases?.length, seen: seenCall, after: after.releases?.length, webNotes })}`);
  // (1.4.3) Settings as the phone's own rows: the options page's Grades and Data through the page's calls
  const si0 = await nc('settingsInfo');
  const si1 = await nc('settingsSave', { tracking: true, goal: 3.456, whatIf: false });
  const hi = await nc('historyImport', { text: 'date,term_gpa\n2026-01-12,3.512\n2026-01-13,3.6' });
  const hi2 = await nc('historyImport', { text: 'date,term_gpa\n2026-01-12,2.0' });
  const grTrend = await nc('grades');
  const he = await nc('historyExport');
  const ri = await nc('recordImport', { text: 'course,grade,credits\nMATH 21,A,4\nWRI 10,B,4\nPE 1,P,1', name: 'past.csv' });
  const rc = await nc('recordClear');
  const se = await nc('settingsExport');
  const sBad = await nc('settingsImport', { text: 'not json' });
  const sGood = await nc('settingsImport', { text: se.text });
  const si2 = await nc('settingsSave', { tracking: si0.tracking, goal: si0.goal, whatIf: si0.whatIf });
  check(grTrend.trend?.length >= 2 && grTrend.trend.every((p) => p.gpaText === half(p.gpa, 2).toFixed(2) && /^\w{3} \d{1,2}(, \d{4})?$/.test(p.dateText)), `the GPA history's days as the chart's readout writes them ("Jan 12 · 3.51"): ${JSON.stringify(grTrend.trend?.map((p) => `${p.dateText} · ${p.gpaText}`))}`);
  check(!si0.error && typeof si0.history === 'string' && typeof si0.recordNote === 'string' && si1.tracking && si1.goal === 3.46 && si1.whatIf === false && !hi.error && /Imported 2 days/.test(hi.message) && hi.info.days >= 2 && /Nothing new/.test(hi2.message) && he.text.split('\n')[0] === 'date,term_gpa' && he.text.includes('2026-01-12,3.512') && !ri.error && /^3\.50 across 2 courses/.test(ri.message) && /1 skipped/.test(ri.message) && /^3\.50 across 2 courses before this term$/.test(ri.info.record) && /past\.csv/.test(ri.info.recordNote) && rc.record === null && rc.tracking && se.name.endsWith('.json') && JSON.parse(se.text).appearance && /not a settings export/.test(sBad.error || '') && sGood.ok && si2.goal === si0.goal && si2.whatIf === si0.whatIf, `Settings in the app: the switches and goal saved and read back, a GPA history imported (a day already here kept) and exported, a record read from a CSV and cleared, the settings exported and imported (a wrong file refused): ${JSON.stringify({ si0, si1: [si1.tracking, si1.goal, si1.whatIf], hi: hi.message || hi.error, hi2: hi2.message, he: he.text?.slice(0, 60), ri: ri.message || ri.error, rec: ri.info?.record, note: ri.info?.recordNote, rc: rc.record, bad: sBad.error, good: sGood })}`);
  // (1.4.8) due-date reminders: what is still to hand in, for the phone to set its own alerts with
  const rm = await nc('reminders');
  const rmNow = Date.now();
  const rmTd = await nc('todo', { showDone: true });
  const rmDone = new Set((rmTd.sections || []).flatMap((s) => s.rows).filter((r) => r.done).map((r) => r.id));
  check(!rm.error && Array.isArray(rm.items) && rm.items.length > 0 && rm.items.every((i) => i.id && i.title && i.kind && i.due && Date.parse(i.due) > rmNow && !rmDone.has(i.id)) && rm.items.every((i, k, a) => k === 0 || a[k - 1].due <= i.due) && new Set(rm.items.map((i) => i.id)).size === rm.items.length, `reminders: work still to hand in, every due time still ahead, nothing done or handed in, soonest first, each once: ${JSON.stringify({ n: rm.items?.length, first: rm.items?.slice(0, 3).map((i) => `${i.kind}:${i.title}@${i.due}`), error: rm.error })}`);
  // (1.5) new activity: what the phone's background check reads Canvas's stream with — the courses chosen, the student's id
  const wi = await nc('watchInfo');
  const wiSel = (await nc('courses')).current?.filter?.((c) => c.on !== false) || [];
  check(!wi.error && wi.me && Array.isArray(wi.courses) && wi.courses.length > 0 && wi.courses.every((c) => c.id && c.name), `the background check's context: the student's id and the courses chosen, each with the name the app shows: ${JSON.stringify({ me: wi.me, courses: wi.courses?.map((c) => `${c.id}:${c.name}`), shown: wiSel.length, error: wi.error })}`);
  // (Mac 1.3.67) every external tool in the student's courses: a course's own tab (Resources & Policy, in each) and a
  // tool found only in an assignment (Knewton Alta, named from its title), each with the courses it is in and how to open it
  const et = await nc('externalTools');
  const rp = et.rows?.find((t) => t.name === 'Resources & Policy'), ka = et.rows?.find((t) => t.name === 'Knewton Alta');
  const kaSprk = ka?.courses.find((c) => c.id === '104');
  check(!et.error && !!rp && rp.courses.length >= 2 && rp.courses.every((c) => c.open.tool === '9' && c.open.course === c.id) && rp.kinds.includes('Course navigation')
    && !!ka && ka.courses.length === 2 && kaSprk?.open.assignment === '4003' && ka.assignments === 5 && ka.kinds.includes('Assignments')
    // (Mac 1.3.77, Windows 0.2.8) every place it is used, to choose from: still to do soonest first, then what is done
    && ka.instances === 5 && kaSprk.items.map((i) => i.id).join() === '4003,4011,4012,4010' && kaSprk.items.every((i) => i.kind === 'assignment' && i.title && i.sub)
    && ka.next?.title === 'Knewton Alta: Unit 2' && rp.instances === 0 && rp.courses.every((c) => c.items.length === 0),
    `external tools: a course's own and one found only in assignments, with where to open each and every place it is used (${JSON.stringify(et.rows?.map((t) => [t.name, t.courses.length, t.kinds.join('+'), t.instances, t.next?.title]))}, ${JSON.stringify(kaSprk?.items?.map((i) => i.id))})`);
  // (Mac 1.3.79, Windows 0.3.1) a tool is its provider: CHEM's Macmillan assignments, titled by their week first ("W2: Atoms
  // and Elements", "W2 HW: Measurement", "W7 Homework: Thermochemistry"; three with the tool's id, one without), its module
  // item and its tab — which the school named "Course Materials", the same tool by its id — are the one tool Macmillan
  // Learning, each place under its own title; no tool is named by the words of an item's title (before 1.3.79: "W2", "W2
  // HW", "W7 Homework", "W7 Pre-lecture" and "Course Materials", five tools); a module's tool whose title names its
  // publisher ("Mastering Physics") is that publisher's
  const mac = et.rows?.find((t) => t.name === 'Macmillan Learning');
  const macChem = mac?.courses.find((c) => c.id === '106');
  const titledTools = new Set(['W2', 'W2 HW', 'W7 Homework', 'W7 Pre-lecture', 'Course Materials', 'Mastering Physics']);
  check(!!mac && mac.provider === 'macmillan' && mac.icon === 'book' && mac.host === 'lmslink.bfwpub.com' && mac.courses.length === 1 && macChem?.open.tool === '31'
    && mac.kinds.join() === 'Course navigation,Assignments,Modules' && mac.assignments === 4 && mac.instances === 5
    && macChem.items.map((i) => `${i.kind}:${i.title}`).join() === 'assignment:W2: Atoms and Elements,assignment:W2 HW: Measurement,assignment:W2: Ions and Molecules,assignment:W7 Homework: Thermochemistry,moduleItem:W7 Pre-lecture: Enthalpy'
    && macChem.items.every((i) => i.short === i.title) && mac.next?.title === 'W2: Atoms and Elements' && macChem.items.filter((i) => i.upcoming).length === 4
    && !et.rows.some((t) => titledTools.has(t.name) || /bfwpub/.test(t.name)) && et.rows.filter((t) => /macmillan/i.test(t.name)).length === 1
    && et.rows.find((t) => t.name === 'Pearson')?.courses.map((c) => c.items.map((i) => i.title).join()).join() === 'Mastering Physics'
    && ka.provider === 'knewton' && rp.provider === null && rp.icon === null,
    `external tools by provider: Macmillan's week-titled assignments, its module item and its tab one tool, Macmillan Learning (${JSON.stringify(et.rows?.map((t) => [t.name, t.provider, t.host, t.kinds.join('+'), t.instances]))}, ${JSON.stringify(macChem?.items?.map((i) => i.title))})`);
  // (Mac 1.3.79, Windows 0.3.1) and with harder Canvas data in CHEM: a launch on Canvas's own route is the tool of the
  // address it hands on or of the id it names; a module's tool with id 0 (one Canvas could not match) joins nobody; two
  // tools of one school are two tools, each by its own host; a title on a site of its own names nothing but before a
  // colon; an unknown publisher's tab the school named "Course Materials" is that publisher's, and a tab named after
  // nothing whose tool's items say "Achieve: …" is Macmillan Learning's
  {
    const hardCtx = await browser.newContext({ viewport: { width: 402, height: 874 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true });
    await hardCtx.addInitScript(nativeStub);
    await hardCtx.addInitScript(shellScript);
    const due = (d) => new Date(Date.now() + d * 864e5).toISOString();
    const asg = (id, name, url, extra = {}) => ({ id, name, due_at: due(30), points_possible: 10, published: true, submission_types: ['external_tool'], html_url: `/courses/106/assignments/${id}`, course_id: 106, external_tool_tag_attributes: { url, new_tab: false, ...extra }, submission: { workflow_state: 'unsubmitted' }, has_submitted_submissions: false });
    const byId = (n) => ({ content_type: 'ContextExternalTool', content_id: n });
    const hard = {
      assignments: [
        asg('7001', 'W2: Atoms', 'https://lmslink.bfwpub.com/lms/launch?i=1', byId(31)),
        asg('7002', 'Quiz 5', `${BASE}/courses/106/external_tools/retrieve?display=borderless&url=${encodeURIComponent('https://lmslink.bfwpub.com/lms/q5')}`),
        asg('7003', 'Week 9: Review', 'https://school.instructure.com/courses/106/external_tools/31'),
        asg('7004', 'Lab 1: Notebook', 'https://jupyter.school.edu/lti', byId(40)),
        asg('7005', 'Reserves: Week 1', 'https://library.school.edu/lti', byId(41)),
        asg('7006', 'Pearson Correlation', 'https://stats.example.org/lti'),
        asg('7007', 'Unit 1: Reading', 'https://lti.newpub.com/launch?u=1', byId(50)),
        asg('7008', 'Achieve: Chapter 3', `${BASE}/courses/106/external_tools/45`, byId(45)),
        asg('7009', 'Week 4 problems', `${BASE}/api/v1/courses/106/external_tools/sessionless_launch?id=45`),
      ],
      modules: [{ id: 'm9', name: 'Week 3', state: 'unlocked', position: 1, items: [
        { id: 'j1', type: 'ExternalTool', content_id: 0, title: 'Class board', external_url: 'https://board.unknown-tool.org/x', html_url: '/courses/106/modules/items/j1' },
        { id: 'j2', type: 'ExternalTool', content_id: 0, title: 'Week 5 activity', external_url: '', html_url: '/courses/106/modules/items/j2' },
        { id: 'j3', type: 'ExternalTool', content_id: 50, title: 'Unit 2: Reading', external_url: '', html_url: '/courses/106/modules/items/j3' },
      ] }],
      tabs: [[31, 'Macmillan'], [50, 'Course Materials'], [45, 'Homework Hub']].map(([n, label], i) => ({ id: `context_external_tool_${n}`, label, html_url: `/courses/106/external_tools/${n}`, type: 'external', position: 20 + i, visibility: 'public' })),
    };
    await hardCtx.route(/\/api\/v1\/courses\/106\/(assignments|modules|tabs)(\?|$)/, async (route) => {
      const what = /\/courses\/106\/(\w+)/.exec(new URL(route.request().url()).pathname)[1];
      if (what !== 'tabs') return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(hard[what]) });
      const own = JSON.parse((await (await route.fetch()).text()).replace(/^while\(1\);/, '')).filter((t) => !String(t.id).startsWith('context_external_tool_') || t.id === 'context_external_tool_9');
      return route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([...own, ...hard.tabs]) });
    });
    const hp = await hardCtx.newPage();
    await hp.goto(`${BASE}/`);
    await hp.waitForFunction(() => document.documentElement.classList.contains('bcv-settled') && !!self.BCVNative, null, { timeout: 25000 });
    const het = await hp.evaluate(() => self.BCVNative.call('externalTools', {}));
    await hardCtx.close();
    const placesOf = (name) => het.rows?.find((t) => t.name === name)?.courses.filter((c) => c.id === '106').flatMap((c) => c.items.map((i) => i.title)).join(' | ');
    const tabOf = (name) => het.rows?.find((t) => t.name === name)?.courses.find((c) => c.id === '106')?.open.tool || null;
    const all106 = (het.rows || []).flatMap((t) => t.courses.filter((c) => c.id === '106').flatMap((c) => c.items.map((i) => i.title)));
    check(!het.error && placesOf('Macmillan Learning') === 'W2: Atoms | Quiz 5 | Week 9: Review | Achieve: Chapter 3 | Week 4 problems'
      && tabOf('Macmillan Learning') === '31' && placesOf('jupyter.school.edu') === 'Lab 1: Notebook' && placesOf('library.school.edu') === 'Reserves: Week 1'
      && placesOf('example.org') === 'Pearson Correlation' && placesOf('unknown-tool.org') === 'Class board' && !all106.includes('Week 5 activity')
      && placesOf('newpub.com') === 'Unit 1: Reading | Unit 2: Reading' && tabOf('newpub.com') === '50'
      && !het.rows.some((t) => ['Course Materials', 'Homework Hub', 'school.edu', 'instructure.com', 'canvas.test', 'localhost'].includes(t.name))
      && het.rows.find((t) => t.name === 'Pearson')?.courses.every((c) => c.id !== '106'),
      `external tools with harder Canvas data: ${JSON.stringify(het.rows?.map((t) => [t.name, t.host, t.courses.find((c) => c.id === '106')?.open, placesOf(t.name)]))}`);
  }
  // More from Canvas: what the school added to Canvas's own nav (the page's #menu — History, an account tool with its
  // icon, Help), for the Mac's and Windows's sidebars, as the web's sidebar shows it (extras.js)
  const mfc = await nc('moreFromCanvas');
  const mfcTool = mfc.items?.find((i) => i.kind === 'tool');
  const mfcOf = (k) => mfc.items?.find((i) => i.kind === k);
  check(!mfc.error && mfc.title === 'More from Canvas' && mfc.from === 'page' && mfc.items.map((i) => `${i.kind}:${i.label}`).join() === 'history:History,tool:My Materials,help:Help'
    && mfcOf('history').open === 'history' && !mfcOf('history').external && mfcOf('help').open === 'help' && mfc.items.every((i) => i.id && i.note)
    && mfcTool.open === 'tool' && mfcTool.external && mfcTool.href === `${BASE}/accounts/1/external_tools/77?launch_type=global_navigation` && mfcTool.url === `${BASE}/accounts/1/external_tools/77?launch_type=global_navigation&display=borderless`
    && mfcTool.icon === `${BASE}/images/tool-icon.svg` && /^data:image\/png;base64,.{200,}/.test(mfcTool.iconData || '') && mfcTool.tile === '#0b2b52',
  `More from Canvas: History, the school's tool (its icon drawn as a PNG, on its nav's colour; opened on Canvas's launch page, borderless) and Help, in the nav's order: ${JSON.stringify(mfc.items?.map((i) => ({ ...i, iconData: i.iconData?.slice(0, 30) })))}`);
  // a tool whose icon Canvas draws inline (its settings give an SVG path, not a picture): the drawing, in the nav's colour
  await sp.evaluate(() => {
    const img = document.querySelector('#menu img');
    self.__toolImg = img;
    img.insertAdjacentHTML('afterend', '<svg class="ic-icon-svg ic-icon-svg--lti" style="fill:rgb(255, 255, 255)" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><path d="M32 4 60 32 32 60 4 32z"/></svg>');
    img.remove();
  });
  const mfcSvg = (await nc('moreFromCanvas')).items?.find((i) => i.kind === 'tool');
  await sp.evaluate(() => { const svg = document.querySelector('#menu svg.ic-icon-svg--lti'); svg.replaceWith(self.__toolImg); });
  const svgText = decodeURIComponent(String(mfcSvg?.icon || '').replace(/^data:image\/svg\+xml;utf8,/, ''));
  check(/^data:image\/svg\+xml;utf8,/.test(mfcSvg?.icon || '') && /fill="rgb\(255, 255, 255\)"/.test(svgText) && /width="64"/.test(svgText) && /^data:image\/png;base64,.{200,}/.test(mfcSvg?.iconData || ''),
    `a tool's inline icon comes as its drawing, in the nav's colour, and as a PNG: ${svgText.slice(0, 160)}`);
  // a page without the menu: the last one seen (written down by extras.js), and nothing at all without one — no section
  await sp.evaluate(() => { const m = document.getElementById('menu'); self.__menuAt = [m, m.parentNode, m.nextSibling]; m.remove(); });
  let mfcSaved = null;
  await eventually(async () => (mfcSaved = await nc('moreFromCanvas')).from === 'saved', 6000);
  const navKept = await sp.evaluate(() => self.BCV.store.pref('globalNav', null));
  await sp.evaluate(() => self.BCV.store.setPref('globalNav', null));
  const mfcNone = await nc('moreFromCanvas');
  await sp.evaluate(([kept]) => { const [m, p, n] = self.__menuAt; p.insertBefore(m, n); return self.BCV.store.setPref('globalNav', kept); }, [navKept]);
  check(mfcSaved.from === 'saved' && mfcSaved.items.map((i) => i.label).join() === 'History,My Materials,Help' && mfcSaved.items.find((i) => i.kind === 'tool')?.url === mfcTool?.url
    && mfcNone.from === 'none' && mfcNone.items.length === 0 && (await nc('moreFromCanvas')).from === 'page',
  `on a page without the menu, the last one seen; with none seen, no items (the apps show no section): ${JSON.stringify({ saved: [mfcSaved?.from, mfcSaved?.items?.length], none: [mfcNone.from, mfcNone.items?.length] })}`);
  // History and Help as the apps' popups: the web's sheets' rows, each course named as the sidebar names it
  const mh = await nc('moreHistory');
  const c101 = (await nc('courses')).rows?.find((r) => r.id === '101');
  check(!mh.error && mh.title === 'History' && mh.note === 'What you opened lately, as Canvas recorded it.' && mh.empty === 'Nothing visited yet.'
    && mh.rows.map((r) => r.title).join('|') === 'Composition of Functions|Lec06-PreQuiz|Week 2 Post Class Assignment: GC articles'
    && mh.rows[0].url === `${BASE}/courses/101/assignments/1002` && mh.rows[1].url === `${BASE}/courses/101/quizzes/9011` && mh.rows.every((r) => r.open === 'place' && r.id && r.when && r.whenText)
    && mh.rows.map((r) => r.kind).join() === 'assignment,quiz,assignment' && mh.rows[0].course === c101?.code && mh.rows[0].color === c101?.color
    && mh.rows[0].sub === `${c101?.code} · Assignment · ${mh.rows[0].whenText}` && mh.rows[1].category === 'Quiz',
  `History: Canvas's recently-visited list, each row where it leads (the app's own screen for it) and its course by the sidebar's name: ${JSON.stringify(mh.rows?.map((r) => [r.title, r.sub, r.url, r.open]))}`);
  const mhp = await nc('moreHelp');
  check(!mhp.error && mhp.title === 'Help' && mhp.rows.map((r) => r.title).join('|') === 'Search the Canvas Guides|IT Help Desk' && mhp.rows[1].sub === 'Campus technology support'
    && mhp.rows.every((r) => r.open === 'browser' && /^https:\/\//.test(r.url)) && /Reporting a problem uses Canvas's own form/.test(mhp.foot) && mhp.note === 'Your school\'s help links, as Canvas lists them.',
  `Help: the school's help links (another site's, for the app's own browser), Canvas's own form left to Canvas: ${JSON.stringify(mhp.rows?.map((r) => [r.title, r.url, r.open]))}`);
  const gp = await nc('groups');
  const gh = await nc('home', { ctx: `groups/${gp.current[0].id}` });
  const ga = await nc('announcements', { ctx: `groups/${gp.current[0].id}` });
  check(!gp.error && gp.current.length > 0 && gp.current.every((g) => g.id && g.name && g.sub) && !gh.error && gh.kind === 'groups' && gh.sections.every((x) => ['announcements', 'discussions', 'pages', 'files', 'people'].includes(x.kind)) && !ga.error, `groups (current and past), a group's home with its own sections, its announcements: ${JSON.stringify({ current: gp.current.map((g) => g.name), past: gp.past.length, sections: gh.sections.map((x) => x.kind) })}`);
  const ib = await nc('inbox', { scope: 'inbox' });
  const cv = await nc('conversation', { id: ib.rows[0].id });
  const rep = await nc('sendReply', { id: ib.rows[0].id, body: 'Thank you, done.' });
  const cv2 = await nc('conversation', { id: ib.rows[0].id });
  check(!ib.error && ib.rows.length > 0 && ib.rows.every((r) => r.subject && r.who && r.when) && !cv.error && cv.messages.length > 0 && cv.messages.every((m) => m.author && 'mine' in m) && rep.ok && cv2.messages.length === cv.messages.length + 1 && cv2.messages[cv2.messages.length - 1].mine, `the Inbox: conversations, one opened oldest first, and a reply from the phone at its end, marked as mine: ${JSON.stringify({ rows: ib.rows.map((r) => r.subject.slice(0, 24)), messages: cv.messages.length, after: cv2.messages.length })}`);
  const ctxs = await nc('composeContexts');
  const sentMsg = await nc('sendMessage', { recipients: ['t101'], subject: 'A question', body: 'Is the midterm open book?', context: 'course_101' });
  check(ctxs.rows.length > 0 && sentMsg.ok && (await nc('sendMessage', { recipients: [], body: 'x' })).error === 'Choose who the message is to.', `a new message from the phone, to a course's teacher (and none without a recipient): ${JSON.stringify(sentMsg)}`);
  // ---- (2.99.35) the apps' live checks: a call's own maxAge, and whether its answer is the one on screen ----
  console.log('live checks (2.99.35)');
  const mock = (path, body) => sp.evaluate(([p, b]) => fetch(p, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(b) }).then((r) => r.text()).then((t) => JSON.parse(t.replace(/^while\(1\);/, ''))), [path, body]);
  const reads = () => sp.evaluate(() => self.BCVNative.stats().reads);
  // a check that finds nothing new: the same answer is the same hash, nothing to decode, and nothing is read while what it
  // is made from is younger than its age
  const live0 = await nc('today', { maxAge: 30, known: '' });
  const reads0 = await reads();
  const live1 = await nc('today', { maxAge: 60, known: live0.hash });
  const reads1 = await reads();
  check(live0.unchanged === false && /^[0-9a-z]+-[0-9a-z]+$/.test(live0.hash || '') && live0.value?.counters?.length === 6 && live1.unchanged === true && live1.hash === live0.hash && !('value' in live1) && reads1 === reads0,
    `a check that finds nothing new: { unchanged: true } with the same hash, no answer to decode, and no read of Canvas while what it is made from is younger than its age: ${JSON.stringify({ first: [live0.unchanged, live0.hash], again: live1, reads: reads1 - reads0 })}`);
  check((await nc('nope', { known: '' })).error === 'No such call: nope' && (await nc('dashList', { maxAge: 60, known: '' })).value?.days?.length > 0, 'a call that fails is { error } with or without known; one that answers is in its envelope');
  // every screen a check reads: the same data makes the same answer, call after call (so the same hash)
  const SCREENS = [['snapshot'], ['todayCounts', { kept: false }], ['todaySheet', { key: 'next' }], ['dashCourses'], ['dashList'], ['dashActivity'], ['dashSkyline'], ['courses'], ['allCourses'], ['coursesProgress'], ['todo'], ['grades'], ['notifications'], ['calendar', { from: new Date(Date.now() - 7 * 864e5).toISOString(), to: new Date(Date.now() + 35 * 864e5).toISOString() }],
    ['groups'], ['externalTools'], ['inbox'], ['home', { ctx: 'courses/101' }], ['announcements', { ctx: 'courses/101' }], ['discussions', { ctx: 'courses/101' }], ['modules', { ctx: 'courses/101' }], ['assignments', { ctx: 'courses/101' }],
    ['pages', { ctx: 'courses/101' }], ['files', { ctx: 'courses/101' }], ['people', { ctx: 'courses/101' }], ['quizzes', { ctx: 'courses/101' }], ['syllabus', { ctx: 'courses/101' }], ['courseGrades', { id: '101' }], ['assignment', { course: '101', id: '1008' }], ['moreHistory']];
  const flips = [];
  for (const [name, args = {}] of SCREENS) {
    const a = await nc(name, { ...args, maxAge: 600, known: '' });
    const b = await nc(name, { ...args, maxAge: 600, known: a.hash });
    if (a.error || !a.hash || b.unchanged !== true) flips.push({ name, a: a.error || a.hash, b: b.error || b.hash });
  }
  check(flips.length === 0, `every screen a check reads answers the same for the same data (${SCREENS.length} calls, each asked twice): ${JSON.stringify(flips)}`);
  // a grade lands behind the engine's back (the mock's own route, not through the store): a call without an age answers
  // from what it read; a call whose age that answer is older than reads Canvas again
  const cs0 = await nc('courses', { maxAge: 30 });
  const row0 = cs0.rows.find((r) => r.score !== null);
  await mock('/__mock/score', { courseId: row0.id, courseScore: 71.3 });
  await sp.waitForTimeout(1100);
  const readsA = await reads();
  const csPlain = await nc('courses');
  const readsB = await reads();
  const csAged = await nc('courses', { maxAge: 1 });
  const readsC = await reads();
  const scoreIn = (cs) => cs.rows?.find((r) => r.id === row0.id)?.scoreText;
  check(scoreIn(csPlain) === row0.scoreText && readsB === readsA && scoreIn(csAged) === '71.3%' && readsC > readsB,
    `without maxAge a call answers from what was read (no read); with one the answer is older than, it reads Canvas again and shows the new total: ${JSON.stringify({ before: row0.scoreText, plain: scoreIn(csPlain), aged: scoreIn(csAged), reads: [readsB - readsA, readsC - readsB] })}`);
  // two calls at once with different ages keep their own: one with an age the course list is older than waits on Canvas
  // (held 600 ms here), while a call beside it without one, and one whose age the list meets, answer from the memo at once
  await mock('/__mock/score', { courseId: row0.id, courseScore: 72.4 });
  await mock('/__mock/config', { apiDelay: { '^/api/v1/courses$': 600 } });
  await sp.waitForTimeout(1100);
  const both = await sp.evaluate(async () => {
    const t0 = performance.now();
    const at = (p) => p.then((v) => ({ v, ms: Math.round(performance.now() - t0) }));
    const A = at(self.BCVNative.call('courses', { maxAge: 1 }));
    await new Promise((r) => setTimeout(r, 60)); // (A is waiting on Canvas now)
    const B = at(self.BCVNative.call('courses', {}));
    const G = at(self.BCVNative.call('grades', { maxAge: 600 }));
    return Promise.all([A, B, G]);
  });
  await mock('/__mock/config', { apiDelay: null });
  const [inA, inB, inG] = both;
  const gradeIn = (g) => g.rows?.find((r) => r.id === row0.id)?.pctText;
  check(scoreIn(inA.v) === '72.4%' && inA.ms >= 550 && scoreIn(inB.v) === '71.3%' && inB.ms < 500 && gradeIn(inG.v) === '71.3%' && inG.ms < 500,
    `interleaved calls with different ages do not leak: the one with the strict age reads Canvas again (and waits on it), the one beside it without an age and the one whose age the memo meets answer from it at once: ${JSON.stringify({ strict: [scoreIn(inA.v), inA.ms], none: [scoreIn(inB.v), inB.ms], grades: [gradeIn(inG.v), inG.ms] })}`);
  // (2.99.35) the Grades overview shows a new total within a check: its course list read again with the age
  await sp.waitForTimeout(1100);
  check(gradeIn(await nc('grades', { maxAge: 1 })) === '72.4%', 'the Grades overview shows a new total on a check (its course list read again with the age)');
  await mock('/__mock/score', { courseId: row0.id, courseScore: row0.score });
  // the change the apps' CI makes between two checks (liveDevChange): written past the store, so only a check sees it
  const t0 = await nc('today', { maxAge: 30, known: '' });
  const done1 = await nc('liveDevChange', { what: 'done' });
  const tPlain = await nc('today', { known: t0.hash });
  await sp.waitForTimeout(1100);
  const tLive = await nc('today', { maxAge: 1, known: t0.hash });
  const key = done1.today ? 'today' : 'next';
  const countOf = (t) => t.counters.find((c) => c.key === key).value;
  check(done1.ok && tPlain.unchanged === true && tLive.unchanged === false && countOf(tLive.value) === countOf(t0.value) - 1 && (!done1.today || (t0.value.list.rows.some((r) => r.id === done1.id) && !tLive.value.list.rows.some((r) => r.id === done1.id))),
    `a dev change marks the soonest work done behind the engine's back: unseen without an age, found by a check (its counter one less, its row gone): ${JSON.stringify({ change: done1, counter: [key, countOf(t0.value), countOf(tLive.value)], plain: tPlain.unchanged })}`);
  await nc('complete', { id: done1.id, done: false });
  const g0 = await nc('dashCourses', { maxAge: 30 });
  const grade1 = await nc('liveDevChange', { what: 'grade' });
  await sp.waitForTimeout(1100);
  const g1 = await nc('dashCourses', { maxAge: 1 });
  const cardOf = (d) => d.rows?.find((r) => r.id === grade1.id);
  check(grade1.ok && cardOf(g1)?.score === grade1.to && cardOf(g0)?.score === grade1.from && g1.rows.filter((r) => r.id !== grade1.id).every((r) => JSON.stringify(r) === JSON.stringify(g0.rows.find((x) => x.id === r.id))),
    `a dev change moves one course's total: a check finds that card changed and every other card the same: ${JSON.stringify({ change: grade1, card: [cardOf(g0)?.scoreText, cardOf(g1)?.scoreText] })}`);
  await mock('/__mock/score', { courseId: grade1.id, courseScore: grade1.from });
  const l0 = await nc('dashList', { maxAge: 30 });
  const task1 = await nc('liveDevChange', { what: 'task' });
  await sp.waitForTimeout(1100);
  const l1 = await nc('dashList', { maxAge: 1 });
  const rowsOf = (d) => d.days.flatMap((x) => x.rows);
  check(task1.ok && rowsOf(l1).length === rowsOf(l0).length + 1 && rowsOf(l1).some((r) => r.id === task1.id && r.title === 'Live check task') && !rowsOf(l0).some((r) => r.id === task1.id),
    `a dev change adds a task of your own for today: a check finds one row more on the List: ${JSON.stringify({ change: task1, rows: [rowsOf(l0).length, rowsOf(l1).length] })}`);
  await nc('deleteTask', { id: task1.id });
  // a topic left open: a check marks it read again only when something in it is unread and the student is at the window
  const puts = () => sp.evaluate(() => self.__puts || 0);
  await sp.evaluate(() => { const put = self.BCV.canvas.put; self.__puts = 0; self.BCV.canvas.put = (p, ...a) => { if (/read_all$/.test(p)) self.__puts++; return put(p, ...a); }; });
  await nc('topic', { ctx: 'courses/101', id: '7004' });
  const opened7004 = await puts();
  await nc('topic', { ctx: 'courses/101', id: '7004', maxAge: 60, seen: true });
  const checked7004 = await puts();
  await nc('topic', { ctx: 'courses/101', id: '7001', maxAge: 60 });
  const away7001 = await puts();
  await nc('topic', { ctx: 'courses/101', id: '7001', maxAge: 60, seen: true });
  const checked7001 = await puts();
  check(opened7004 === 1 && checked7004 === 1 && away7001 === 1 && checked7001 === 2, `a topic opened is marked read; a check of it marks it read again only when Canvas says something in it is unread (7004 is read, 7001 not yet) and the student is there (seen): ${JSON.stringify({ opened7004, checked7004, away7001, checked7001 })}`);
  // a conversation left open: a check reads it without Canvas marking it read (auto_mark_as_read=false), and marks it
  // read itself only while the student is there (seen); opened, it is read the plain way
  const convLog = await sp.evaluate(async () => {
    const C = self.BCV.canvas, get = C.get, put = C.put, log = [];
    const settle = () => new Promise((r) => setTimeout(r, 300));
    C.get = (p, o) => { if (/\/conversations\/c1$/.test(p)) log.push(o?.params?.auto_mark_as_read === false ? 'kept unread' : 'plain'); return get(p, o); };
    C.put = (p, b, ...a) => { if (/\/conversations\/c1$/.test(p) && b?.conversation?.workflow_state === 'read') log.push('marked'); return put(p, b, ...a); };
    try {
      await put('/api/v1/conversations/c1', { conversation: { workflow_state: 'unread' } });
      await self.BCVNative.call('conversation', { id: 'c1', maxAge: 60 });
      await settle();
      log.push('|');
      await self.BCVNative.call('conversation', { id: 'c1', maxAge: 60, seen: true });
      await settle();
      log.push('|');
      await self.BCVNative.call('conversation', { id: 'c1' });
      await settle();
    } finally { C.get = get; C.put = put; }
    return log.join(' ');
  });
  check(convLog === 'kept unread | kept unread marked | plain', `a conversation's check leaves it unread while nobody is at the window, and marks it read once the student is: ${convLog}`);
  check((await nc('liveDevChange', { what: 'nothing' })).error === 'No such change: nothing', 'a dev change of no known kind is refused');
  // (1.2) no Tools on the iPhone yet: no row, no route, no command, no Convert on a file
  await sp.evaluate(() => self.BCV.lazy?.load?.('hub'));
  const toolsOff = await sp.evaluate(() => {
    const T = ['tool', 'tools', 'pin', 'unpin', 'convert'];
    return {
      byName: T.filter((n) => self.BCV.hub.byName(n)),
      listed: self.BCV.hub.matchCommands('').map((c) => c.name).filter((n) => T.includes(n)),
      route: self.BCV.app.parseRoute(`${location.origin}/#tools`).screen,
      fileActions: self.BCV.hub.actionsFor({ file: { id: 1, url: '/files/1/download' } }).map((a) => a.label),
      kept: ['inbox', 'open', 'download'].filter((n) => self.BCV.hub.byName(n)),
    };
  });
  check(toolsOff.byName.length === 0 && toolsOff.listed.length === 0 && toolsOff.route === 'dashboard' && !toolsOff.fileActions.includes('Convert') && toolsOff.fileActions.includes('Download') && toolsOff.kept.length === 3, `no Tools in the app yet: /tool, /tools, /pin, /unpin and /convert are not offered, /#tools is the Dashboard, a file has no Convert (the rest of the commands stay, /open for a course's tools among them): ${JSON.stringify(toolsOff)}`);
  // navigation: a press in the page becomes a screen on the app's stack; the app shows a screen by asking the page to draw it
  await sp.evaluate(() => { self.__nativeCalls.length = 0; });
  const shown = await sp.evaluate(() => self.BCVNative.show('/courses/101'));
  await sp.waitForFunction(() => location.pathname === '/courses/101' && document.documentElement.classList.contains('bcv-settled'), null, { timeout: 15000 });
  const page1 = await eventually(async () => (await told('shell.page')).some((p) => /\/courses\/101$/.test(new URL(p.url).pathname) && p.title), 6000);
  const pageMsg = (await told('shell.page')).pop();
  const hist = await sp.evaluate(() => history.length);
  check(shown.ok && page1 && pageMsg.screen === 'course' && pageMsg.root === false && !!pageMsg.title, `the app shows a course: drawn in place, its title said for the app's bar: ${JSON.stringify(pageMsg)}`);
  await sp.click('.bcv-ph-tab[data-tab="files"]');
  await sp.waitForTimeout(500);
  const opened = (await told('shell.open')).pop();
  check(!!opened && /\/courses\/101\/files$/.test(new URL(opened.url).pathname) && new URL(sp.url()).pathname === '/courses/101', `a press in the page asks the app for a new screen (the page stays where it is until the app shows it): ${JSON.stringify(opened)}`);
  await sp.evaluate(() => self.BCV.app.go('/courses'));
  await sp.waitForTimeout(300);
  const tabbed = (await told('shell.tab')).pop();
  check(tabbed?.tab === 'courses', `a press that leads to a root screen switches the app's tab instead: ${JSON.stringify(tabbed)}`);
  await sp.evaluate(() => self.BCVNative.show('/courses/101/files'));
  await sp.waitForFunction(() => location.pathname === '/courses/101/files' && document.documentElement.classList.contains('bcv-settled'), null, { timeout: 15000 });
  check((await sp.evaluate(() => history.length)) === hist, 'a screen the app shows replaces the page\'s entry (the app\'s stack is the history)');
  await sp.screenshot({ path: join(out, '23a-ios-shell-files.png') });
  // Canvas's banners with the app's own screens: this page is the app's engine, under them, so nothing of Canvas's comes up
  // as a toast here (it would never be seen, and the phone would buzz for it) — being signed out has Canvas asked whether
  // the session is gone instead. (?mockflash=1: Canvas's failed request and its planner's talk, mock-canvas.mjs flashScript)
  await sp.goto(`${BASE}/?mockflash=1`);
  await sp.waitForFunction(() => document.documentElement.classList.contains('bcv-settled') && !!document.getElementById('flashalert_message_holder'), null, { timeout: 25000 });
  await sp.waitForTimeout(500);
  await sp.evaluate(() => {
    const ask = self.BCV.canvas.checkSession;
    self.__sessionAsked = 0;
    self.BCV.canvas.checkSession = (...a) => { self.__sessionAsked++; return ask(...a); };
    const put = (w) => document.getElementById('flash_message_holder').insertAdjacentHTML('beforeend', `<li class="ic-flash-error">${w}<button type="button" class="close_link" aria-label="Close"></button></li>`);
    put('You are not authorized to perform that action.');
    put('You are not currently logged in, possibly due to a long period of inactivity.<br/><a href="/login" id="inactivity_login_link" target="_new">Login</a>');
  });
  await sp.waitForTimeout(500);
  const appFlash = await sp.evaluate(() => ({
    toasts: [...document.querySelectorAll('.bcv-toast')].map((t) => t.textContent.trim()).filter((w) => /Oops|Nothing planned|authorized|logged in/.test(w)),
    left: [...document.querySelectorAll('#flashalert_message_holder > *, #flash_message_holder > *')].map((e) => e.textContent.trim()).filter(Boolean),
    buzz: self.__nativeCalls.filter((c) => c.op === 'haptic' && c.kind === 'error').length,
    asked: self.__sessionAsked,
    shell: document.documentElement.classList.contains('bcv-native-shell'),
  }));
  check(appFlash.shell && !appFlash.toasts.length && !appFlash.left.length && !appFlash.buzz && appFlash.asked === 1, `with the app's own screens, Canvas's banners are taken off the page and none comes up as a toast the app would hide (no buzz for one); being signed out asks Canvas, once, whether the session is gone: ${JSON.stringify(appFlash)}`);
  check(shellErrors.length === 0,`no page errors with the shell${shellErrors.length ? `: ${shellErrors.slice(0, 3).join(' | ')}` : ''}`);
  await shellCtx.close();

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
    stale: '<h1>UC MERCED Single Sign On</h1><h2>Web Login Service - Stale Request</h2><p>You may be seeing this page because you used the Back button while browsing a secure web site or application.</p>',
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
  m = await open('stale');
  check(m.length === 1 && m[0].kind === 'none' && m[0].stale === true, `a single sign-on page saying the request went stale (Shibboleth's "Stale Request") is said as stale, for the app to start the sign-in again: ${JSON.stringify(m)}`);
  m = await open('code');
  check(m.length === 1 && m[0].stale === false, `a code page is not stale: ${JSON.stringify(m)}`);
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

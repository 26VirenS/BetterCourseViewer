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
  check(!convInfo.error && convInfo.exts.includes('txt') && /converted to PDF/.test(convInfo.line) && converted.converted === true && converted.name === 'notes.pdf' && pdfHead === '%PDF-', `the hand-in converts a file of another type to one the assignment takes: ${JSON.stringify({ exts: convInfo.exts, line: convInfo.line, name: converted.name, type: converted.type, head: pdfHead, error: converted.error })}`);
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
  check((await nc('reply', { ctx, id: '7003', text: '   ' })).error === 'Write a reply first.', 'an empty reply is refused with a reason');
  const mods = await nc('modules', { ctx });
  const markable = mods.modules.flatMap((m) => m.items.map((it) => ({ m, it }))).find((x) => x.it.markable);
  const marked = markable ? await nc('markDone', { ctx, module: markable.m.id, item: markable.it.id, done: true }) : null;
  const mods2 = await nc('modules', { ctx });
  const nowDone = mods2.modules.flatMap((m) => m.items).find((it) => it.id === markable?.it.id);
  check(!mods.error && mods.modules.length > 0 && mods.modules.every((m) => m.items.every((it) => it.header || it.url)) && !mods.modules.flatMap((m) => m.items).some((it) => /undefined/.test(it.url || '')) && !!markable && marked?.ok && nowDone?.done === true, `modules with every item's address and requirement; a "Mark done" item marked from the phone: ${JSON.stringify({ modules: mods.modules.map((m) => `${m.name}:${m.items.length}`), marked: markable?.it.title, done: nowDone?.done })}`);
  if (markable) await nc('markDone', { ctx, module: markable.m.id, item: markable.it.id, done: false });
  const asg = await nc('assignments', { ctx });
  check(!asg.error && asg.sections.length > 0 && asg.sections.every((x) => ['Overdue', 'Upcoming', 'Undated', 'Past'].includes(x.title)) && asg.sections.flatMap((x) => x.rows).every((r) => r.status?.word && /\/assignments\/\d+$/.test(r.url)), `assignments by when they are due, each with its status: ${JSON.stringify(asg.sections.map((x) => `${x.title}:${x.rows.length}`))}`);
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
  // (Mac 1.3.8) a tool's assignment Canvas says cannot be submitted (can_submit: false, as of every tool's) is not called closed
  const toolA = await nc('assignment', { course: '104', id: '4003' });
  check(!toolA.error && !!toolA.toolUrl && toolA.why !== 'This assignment is closed.' && !toolA.canSubmit, `a tool's assignment open in the tool is not "closed": ${JSON.stringify({ why: toolA.why, tool: !!toolA.toolUrl, canSubmit: toolA.canSubmit })}`);
  // (Mac 1.3.7) Previous / Next: an assignment's neighbours in the course's own order, each a name and an assignment's address
  const around = await nc('neighbours', { ctx: 'courses/101', type: 'Assignment', id: '1012' });
  const pageAround = await nc('neighbours', { ctx: 'courses/101', type: 'Page', id: 'nope-no-such-page' });
  check(!around.error && (around.prev || around.next) && [around.prev, around.next].filter(Boolean).every((x) => x.name && /\/courses\/101\/assignments\/\d+/.test(x.url)) && !pageAround.error, `Previous / Next for the Mac: an assignment's neighbours, and nothing (not an error) for a page it cannot place: ${JSON.stringify({ around, pageAround })}`);
  const a12 = await nc('assignment', { course: '101', id: '1012' });
  check(!a12.error && a12.canSubmit && a12.types.includes('online_text_entry') && a12.types.includes('online_upload') && a12.status?.word && a12.due && a12.points, `an assignment for the app: its facts, its instructions, and the ways it can be handed in from the phone: ${JSON.stringify({ title: a12.title, types: a12.types, due: a12.due, points: a12.points, status: a12.status })}`);
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
  check(!/\\/.test(ma.plain) && /vector quantities, such as v\?/.test(ma.plain), `the review's line reads a formula as maths, not LaTeX commands: ${JSON.stringify(ma.plain)}`);
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
  check(!su.error && su.done === true && su.courses.length > 0 && onIds.length > 0 && su.grades.join() === 'C,B,B+,A-,A,A+' && saved1.ok && saved1.courses === onIds.length && su2.courses.find((c) => c.id === first)?.target === 'B+' && su2.goal === 3.7 && su2.courses.filter((c) => c.on).length === onIds.length && snapSetup.setupDone === true && saved2.ok, `the iPhone's setup: the current courses (the chosen ones ticked), the targets and the goal written and read back, the courses left as chosen, the snapshot says it is done: ${JSON.stringify({ courses: su.courses?.length, on: onIds.length, target: su2.courses?.find((c) => c.id === first)?.target, goal: su2.goal, setupDone: snapSetup.setupDone, error: su.error || saved1.error })}`);
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
  const he = await nc('historyExport');
  const ri = await nc('recordImport', { text: 'course,grade,credits\nMATH 21,A,4\nWRI 10,B,4\nPE 1,P,1', name: 'past.csv' });
  const rc = await nc('recordClear');
  const se = await nc('settingsExport');
  const sBad = await nc('settingsImport', { text: 'not json' });
  const sGood = await nc('settingsImport', { text: se.text });
  const si2 = await nc('settingsSave', { tracking: si0.tracking, goal: si0.goal, whatIf: si0.whatIf });
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
  check(shellErrors.length === 0, `no page errors with the shell${shellErrors.length ? `: ${shellErrors.slice(0, 3).join(' | ')}` : ''}`);
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

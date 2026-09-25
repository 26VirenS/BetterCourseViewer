#!/usr/bin/env node
// The phone layout (the iPhone mockup): loads the extension into headless Chromium at a phone
// viewport against the mock Canvas server, walks the five tabs, Notifications, a course, an item
// (with the submit block on the same page), a quiz, the sheets, the swipe actions and the edge
// swipe, and saves screenshots to scripts/dev/out/phone-*.png.
// Requires Playwright (project or global install).
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { mkdirSync, cpSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { TIMERS, shortenTimers, secs, reportSlow, afterMigration } from './harness.mjs';

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
const PORT = 8792; // (its own: the smoke suite's tool site is on 8791, and the two run side by side under test-all.mjs)
const BASE = `http://localhost:${PORT}`;

// 1. temp copy of the extension whose content scripts also match localhost
const extDir = join(tmpdir(), `bcv-phone-ext-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*')) cs.matches.push(`${BASE}/*`); // (the on-demand modules keep their never-matching group: a page asks for them, as it does in a browser)
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
const timers = shortenTimers(extDir); // the product's longest waits run short in this copy (harness.mjs)

// 2. mock server
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'inherit' });
await new Promise((r) => setTimeout(r, 600));

const failures = [];
const startedAt = Date.now();
let lastCheckAt = startedAt;
const check = (cond, label) => { // a check long after the one before says so, in seconds
  const now = Date.now();
  const gap = now - lastCheckAt;
  lastCheckAt = now;
  console.log(`${cond ? '  ✓' : '  ✗'} ${label}${gap >= 1500 ? `  (+${secs(gap)})` : ''}`);
  if (!cond) failures.push(label);
};
console.log('harness');
check(timers.missing.length === 0 && timers.done.length === 12, `the copy runs the product's longest timers short, the shipped values found exactly (${timers.done.length} rewritten${timers.missing.length ? `; not found: ${timers.missing.join(' | ')}` : ''})`);

const userDataDir = join(tmpdir(), `bcv-phone-profile-${Date.now()}`);
// an iPhone-sized viewport with touch; the layout switches on width (≤700px) before first paint
const context = await chromium.launchPersistentContext(userDataDir, {
  channel: 'chromium',
  headless: true,
  viewport: { width: 402, height: 874 },
  deviceScaleFactor: 2,
  isMobile: true,
  hasTouch: true,
  args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`],
});
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  await afterMigration(sw); // (the background's setup migration first, or it clears the flags written next)
  const setSettings = (patch) => sw.evaluate(async (p) => self.BCV.settings.update(p), patch);
  // until it is done every page opens the setup; it is exercised on its own below. The flow marker goes with the flags: the
  // background's one-time migration clears them when it finds an older flow, and it may run after this.
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'setup:flow': 3, 'whatsnew:seen': v }), manifest.version);

  const page = await context.newPage();
  // a page is ready to poke once it is drawn, nothing painted from the cache is still waiting on
  // Canvas, and no counter is still rolling to its value
  const settled = () => page.waitForFunction(() => { const c = document.documentElement.classList; return !c.contains('bcv-on') || c.contains('bcv-settled'); }, null, { timeout: 20000 }).catch(() => {});
  const rolled = () => page.waitForFunction(() => !document.querySelector('[data-rolling]'), null, { timeout: 6000 }).catch(() => {});
  const __goto = page.goto.bind(page);
  page.gotoRaw = __goto;
  page.goto = async (...a) => { const r = await __goto(...a); await settled(); await rolled(); return r; };
  reportSlow(page); // any one wait of three seconds or more is named in the log
  const errors = [];
  page.on('pageerror', (e) => { errors.push(e.message); console.log('  page error:', e.message); });
  // the mock answers one calendar context with 401 on purpose (the retry path); a failed resource load is not a script error
  page.on('console', (m) => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) { errors.push(m.text()); console.log('  console:', m.text()); } });
  const texts = (sel) => page.$$eval(sel, (els) => els.map((e) => (e.innerText || e.textContent).replace(/\s+/g, ' ').trim()));
  const raw = (sel) => page.$$eval(sel, (els) => els.map((e) => e.textContent.replace(/\s+/g, ' ').trim())); // as written (kickers are uppercased by CSS)
  const visible = (sel) => page.$eval(sel, (el) => { const r = el.getBoundingClientRect(); return getComputedStyle(el).display !== 'none' && getComputedStyle(el).visibility !== 'hidden' && r.width > 0 && r.height > 0; }).catch(() => false);
  const settle = () => page.evaluate(() => Promise.all(document.getAnimations().map((a) => a.finished.catch(() => {}))).then(() => new Promise((r) => requestAnimationFrame(() => r()))));
  const shot = async (name) => { await settle(); await page.screenshot({ path: join(out, `phone-${name}.png`) }); };
  const eventually = async (fn, ms = 6000) => {
    const t = Date.now();
    while (Date.now() - t < ms) {
      if (await fn()) return true;
      await new Promise((r) => setTimeout(r, 60));
    }
    return false;
  };
  // tap something that lands a new screen (a page load or a hash change), and wait until it has drawn
  const tapScreen = async (sel) => {
    await page.evaluate(() => { const m = document.querySelector('#bcv-main > *'); if (m) m.dataset.old = '1'; });
    await page.click(sel);
    await page.waitForSelector('#bcv-main > *:not([data-old])', { timeout: 10000 });
    await settled();
    await rolled();
  };
  const tab = (id) => tapScreen(`.bcv-tabbar__item[data-tab="${id}"]`);
  // The background's one-time migration can clear the setup flags after they were written above, and
  // the card it then opens sits over everything, whenever it happens to run — put them back and load
  // the page again. Every check still runs against the app itself; only the card is cleared.
  const noSetup = async () => {
    if (!(await page.$('#bcv-setup'))) return false;
    await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'setup:flow': 3, 'whatsnew:seen': v }), manifest.version);
    await page.goto(page.url());
    return true;
  };
  const ready = async () => { await noSetup(); return page.waitForSelector('#bcv-app .bcv-tabbar__item', { timeout: 15000 }); };
  const sheet = () => page.waitForSelector('.bcv-sheet-ov .bcv-ph-sheet', { timeout: 5000 });
  // The counters are redrawn when their counts land, so a press can meet a card on its way out and
  // wait for a steady box that never comes. Every actionability check still has to pass — the press
  // is only tried again, against whatever card is there now.
  const press = async (sel) => {
    for (let i = 0; i < 3; i++) {
      try { await page.click(sel, { timeout: 7000 }); return; } catch { await noSetup(); await settle(); await rolled(); }
    }
    await page.click(sel, { timeout: 7000 });
  };
  const closeSheet = async () => { await page.keyboard.press('Escape'); await eventually(async () => !(await page.$('.bcv-sheet-ov'))); };
  const noOverflow = () => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);
  // a finger dragging a row left: 1:1 during the drag, latching open past half the tray
  const swipeLeft = async (sel, dx = 110) => {
    const box = await page.$eval(sel, (el) => { const r = el.getBoundingClientRect(); return { x: r.left + r.width * 0.6, y: r.top + r.height / 2 }; });
    await page.mouse.move(box.x, box.y);
    await page.mouse.down();
    await page.mouse.move(box.x - dx, box.y, { steps: 8 });
    await page.mouse.up();
  };
  const layout = () => page.evaluate(() => ({
    phone: document.documentElement.classList.contains('bcv-phone'),
    root: document.documentElement.classList.contains('bcv-ph-root'),
    tabbar: getComputedStyle(document.getElementById('bcv-tabbar')).position,
    glass: /blur/.test(getComputedStyle(document.getElementById('bcv-tabbar')).backdropFilter || getComputedStyle(document.getElementById('bcv-tabbar')).webkitBackdropFilter || ''),
    topbarHidden: document.getElementById('bcv-topbar')?.hidden,
    side: !!document.querySelector('#bcv-side'),
    rail: !!document.querySelector('.bcv-rail') && getComputedStyle(document.querySelector('.bcv-rail')).display !== 'none',
  }));

  // ---- Today ------------------------------------------------------------------------------------
  console.log('Today');
  await page.goto(`${BASE}/`);
  await ready();
  await page.waitForSelector('.bcv-ph-stat', { timeout: 15000 });
  await rolled();
  let l = await layout();
  check(l.phone && l.root && l.tabbar === 'fixed' && l.glass && l.topbarHidden === true && !l.side, `phone layout: html.bcv-phone, root screen, a fixed glass tab bar, no top bar, no sidebar (${JSON.stringify(l)})`);
  check((await texts('.bcv-tabbar__item')).join(',') === 'Today,Courses,To Do,Grades,Calendar', `five tabs: ${(await texts('.bcv-tabbar__item')).join(', ')}`);
  check((await page.$eval('.bcv-tabbar__item.is-active', (e) => e.dataset.tab)) === 'dashboard', 'Today is the active tab');
  check((await texts('.bcv-ph-h1'))[0] === 'Today' && /^(Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday), \w+ \d+$/.test((await texts('.bcv-ph-title__sub'))[0]), `large title with the date line: ${(await texts('.bcv-ph-title__sub'))[0]}`);
  const stats = await texts('.bcv-ph-stat');
  check(stats.length === 3 && /^Due today \d+$/.test(stats[0]) && /^This week \d+$/.test(stats[1]) && /^Unread \d+$/.test(stats[2]), `three counters, rolled to their values: ${stats.join(' | ')}`);
  check((await texts('.bcv-ph-ghead__t')).some((t) => /^(Today|Tonight|Next up)$/.test(t)) && (await page.$$('.bcv-ph-row')).length > 0, `the day's list: ${(await texts('.bcv-ph-ghead__t')).join(', ')}`);
  check((await raw('.bcv-ph-kicker')).includes('Week load') && (await page.$$('.bcv-ph-load__row')).length > 0, 'week load card with per-course bars');
  check(await visible('.bcv-ph-bell') && await visible('.bcv-ph-avatar') && !(await page.$('.bcv-reader-btn:not([hidden])')), 'the bell and the avatar on the title row; no reader button anywhere on the phone');
  check(await noOverflow(), 'no horizontal overflow');
  await shot('01-today');

  await press('.bcv-ph-stat:nth-child(2)');
  await sheet();
  check((await texts('.bcv-ph-sheet__title'))[0] === 'Due this week' && (await page.$$('.bcv-ph-srow')).length > 0 && await visible('.bcv-ph-sheet__handle'), 'a counter opens its list as a bottom sheet with a grab handle');
  await shot('01b-today-sheet');
  // dragging the handle down past 110px dismisses; a short drag springs back
  const handle = await page.$eval('.bcv-ph-sheet__handle', (el) => { const r = el.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; });
  await page.mouse.move(handle.x, handle.y); await page.mouse.down(); await page.mouse.move(handle.x, handle.y + 40, { steps: 4 }); await page.mouse.up();
  await new Promise((r) => setTimeout(r, 350));
  check(!!(await page.$('.bcv-sheet-ov')), 'a short drag on the handle springs the sheet back');
  await page.mouse.move(handle.x, handle.y); await page.mouse.down(); await page.mouse.move(handle.x, handle.y + 160, { steps: 6 }); await page.mouse.up();
  check(await eventually(async () => !(await page.$('.bcv-sheet-ov'))), 'a drag past 110px dismisses it');
  await press('.bcv-ph-stat:nth-child(2)');
  await sheet();
  await closeSheet();
  check(!(await page.$('.bcv-sheet-ov')), 'Escape closes the sheet');

  // the circle marks a row done (a planner override) and the row stays
  const firstRow = (await texts('.bcv-ph-row__title'))[0];
  await page.click('.bcv-ph-row .bcv-ph-circle');
  check(await eventually(() => page.$eval('.bcv-ph-row', (e) => e.classList.contains('is-done'))), `the circle marks "${firstRow}" done`);
  await page.click('.bcv-ph-row .bcv-ph-circle');
  await eventually(() => page.$eval('.bcv-ph-row', (e) => !e.classList.contains('is-done')));

  // a row previews the item first: a phone has no room beside the list, so it rises from the bottom
  const rowTitle = await page.$eval('.bcv-ph-row__title', (e) => e.textContent);
  await page.click('.bcv-ph-row__body');
  await page.waitForSelector('.bcv-ph-sheet--pv .bcv-pv__title', { timeout: 10000 });
  await page.waitForFunction(() => !document.querySelector('.bcv-pv .bcv-skel'), null, { timeout: 10000 });
  const pv = await page.$eval('.bcv-ph-sheet--pv', (e) => ({
    title: e.querySelector('.bcv-pv__title').textContent,
    meta: e.querySelector('.bcv-pv__meta').textContent,
    go: e.querySelector('.bcv-ph-sheet__actions .bcv-ph-bigbtn').textContent,
    has: !!e.querySelector('.bcv-pv__prose, .bcv-pv__none'),
    wide: Math.round(e.getBoundingClientRect().width) === Math.round(window.innerWidth),
  }));
  check(pv.title === rowTitle && /^(Assignment|Quiz|Discussion|Announcement|Page) · /.test(pv.meta) && /^Open the /.test(pv.go) && pv.has && pv.wide && page.url().endsWith('/'), `a row previews the item in a sheet rather than leaving Today: ${JSON.stringify(pv)}`);
  check(!(await page.$eval('html', (e) => e.classList.contains('bcv-preview'))), 'the phone never shifts the interface for it: the sheet carries the preview');
  // the button under it is the way through
  await page.click('.bcv-ph-sheet__actions .bcv-ph-bigbtn');
  await page.waitForFunction(() => !location.pathname.endsWith('/') && !document.querySelector('.bcv-sheet-ov'), null, { timeout: 15000 });
  check(/\/courses\/\d+\/(assignments|quizzes|discussion_topics|announcements|pages)\//.test(page.url()), `the button under the preview opens the item: ${page.url()}`);
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-ph-row', { timeout: 15000 });

  // ---- Notifications (from the bell) ------------------------------------------------------------
  console.log('Notifications');
  const bellBadge = (await texts('.bcv-ph-bell__badge'))[0] || '';
  await tapScreen('.bcv-ph-bell');
  await page.waitForSelector('.bcv-ph-nfchip', { timeout: 15000 });
  l = await layout();
  check(!l.root && l.topbarHidden === false && (await texts('.bcv-topbar__back'))[0] === 'Today' && (await texts('.bcv-topbar__title'))[0] === 'Notifications', `the bell pushes Notifications under a back bar: ‹ ${(await texts('.bcv-topbar__back'))[0]} · ${(await texts('.bcv-topbar__title'))[0]}`);
  const chips = await texts('.bcv-ph-nfchip');
  check(chips.length > 1 && /^All \d+$/.test(chips[0]) && (await page.$eval('.bcv-ph-nfchip.is-on', (e) => e.dataset.cat)) === 'all', `category chips with counts: ${chips.join(' · ')}`);
  const nfCount = (await texts('.bcv-ph-nfcount__t'))[0];
  check(/^\d+ notifications? · \d+ unread$/.test(nfCount) && (await page.$$('.bcv-ph-nfrow')).length > 0 && (await texts('.bcv-ph-nfgroup__t')).length > 0, `count line, rows grouped by day: ${nfCount} · ${(await texts('.bcv-ph-nfgroup__t')).join(', ')}`);
  check(bellBadge === '' || nfCount.endsWith(`${bellBadge} unread`), `the bell's badge (${bellBadge || 'none'}) matches the unread count`);
  await shot('01e-notifications');
  await page.click('.bcv-ph-nfchip:nth-child(2)');
  const cat = await page.$eval('.bcv-ph-nfchip.is-on', (e) => e.dataset.cat);
  check(cat !== 'all' && await eventually(() => page.$$eval('.bcv-ph-nfrow', (els, c) => els.length > 0 && els.every((e) => e.dataset.cat === c), cat)), `a chip filters to its category (${cat})`);
  await page.click('.bcv-ph-nfchip[data-cat="all"]');
  await eventually(async () => (await page.$$('.bcv-ph-nfrow')).length > 1);
  // swipe left reveals Read / Clear; Read marks it read
  const rowId = await page.$eval('.bcv-ph-nfrow:not(.is-read)', (e) => e.dataset.id);
  await swipeLeft(`.bcv-ph-nfrow[data-id="${rowId}"]`);
  check(await eventually(() => page.$eval(`.bcv-ph-nfrow[data-id="${rowId}"]`, (e) => e.closest('.bcv-ph-swipe').classList.contains('is-open'))) && (await texts('.bcv-ph-swipe.is-open .bcv-ph-swipe__act')).join(',') === 'Read,Clear', 'swiping a notification left latches its Read / Clear actions open');
  await shot('01f-notification-swipe');
  await page.click('.bcv-ph-swipe.is-open .bcv-ph-swipe__act:first-child');
  check(await eventually(() => page.$eval(`.bcv-ph-nfrow[data-id="${rowId}"]`, (e) => e.classList.contains('is-read'))), 'Read marks the row read');
  const unreadBefore = Number((await texts('.bcv-ph-nfcount__t'))[0].match(/(\d+) unread/)[1]);
  await page.click('#bcv-nf-readall');
  check(await eventually(async () => /· 0 unread$/.test((await texts('.bcv-ph-nfcount__t'))[0])), `Read all clears the unread count (${unreadBefore} → 0)`);
  await page.click('.bcv-topbar__back');
  await page.waitForSelector('.bcv-ph-stat', { timeout: 15000 });
  await rolled();
  check((await page.$eval('.bcv-ph-bell__badge', (e) => e.hidden)) === true, 'back on Today the bell has no badge left');

  // ---- account sheet + appearance -------------------------------------------------------------
  console.log('account sheet');
  await page.click('.bcv-ph-avatar');
  await sheet();
  check((await texts('.bcv-ph-me__name'))[0] === 'Sam Student (they/them)' && (await texts('.bcv-ph-me__sub'))[0] === 'sstudent@ucmerced.edu', `the sheet's head carries the pronouns and the e-mail Canvas holds: ${(await texts('.bcv-ph-me__name'))[0]} · ${(await texts('.bcv-ph-me__sub'))[0]}`);
  const acct = await texts('.bcv-ph-srow__label');
  check(acct.join(',') === 'Inbox,Groups,Tools,History,My Materials,Help,Dark appearance,Settings,Guided setup,What’s new,Profile,All Canvas settings,Log out', `account sheet rows, with Tools and the school's own nav entries: ${acct.join(', ')} (no Sign out outside the app)`);
  check((await texts('.bcv-ph-srow__note'))[0] === 'No unread messages' || /unread message/.test((await texts('.bcv-ph-srow__note'))[0]), `Inbox row carries the unread count: ${(await texts('.bcv-ph-srow__note'))[0]}`);
  await shot('01c-account-sheet');
  // the Settings row opens Simpl's settings (it called a function that never existed, and did nothing)
  await sw.evaluate(async (base) => { const [t] = await chrome.tabs.query({ url: `${base}/*` }); await chrome.scripting.executeScript({ target: { tabId: t.id }, world: 'ISOLATED', func: () => { self.__settingsOpened = 0; self.BCV.app.openSettings = () => { self.__settingsOpened += 1; }; } }); }, BASE);
  await page.click('.bcv-ph-srow:has-text("Settings")');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet-ov'), null, { timeout: 5000 });
  const settingsOpened = await sw.evaluate(async (base) => { const [t] = await chrome.tabs.query({ url: `${base}/*` }); const [{ result }] = await chrome.scripting.executeScript({ target: { tabId: t.id }, world: 'ISOLATED', func: () => self.__settingsOpened }); return result; }, BASE);
  check(settingsOpened === 1, 'the account sheet’s Settings row opens Simpl settings');
  await page.click('.bcv-ph-avatar');
  await sheet();
  await page.evaluate(() => { window.__bcvMarker = 1; });
  await page.click('.bcv-ph-srow:has-text("Dark appearance")');
  await page.waitForFunction(() => document.documentElement.getAttribute('data-bcv-theme') === 'dark' && window.__bcvMarker === undefined && document.querySelector('.bcv-ph-stat'), null, { timeout: 15000 });
  check(true, 'Dark appearance switches the theme and reloads the page (the web versions reload on an appearance change)');
  await ready();
  await page.waitForSelector('.bcv-ph-stat', { timeout: 15000 });
  await rolled();
  await shot('01d-today-dark');
  await page.click('.bcv-ph-avatar');
  await sheet();
  check((await texts('.bcv-ph-srow__label')).includes('Light appearance'), 'the sheet offers the way back');
  await closeSheet();
  await page.evaluate(() => { window.__bcvMarker = 1; });
  await setSettings({ appearance: { darkMode: 'off' } });
  await page.waitForFunction(() => document.documentElement.getAttribute('data-bcv-theme') === 'light' && window.__bcvMarker === undefined && document.querySelector('.bcv-ph-stat'), null, { timeout: 15000 });
  check(true, 'a change from the popup or settings reloads too');
  await ready();

  // ---- Courses ----------------------------------------------------------------------------------
  console.log('Courses');
  await tab('courses');
  await page.waitForSelector('.bcv-ph-crow', { timeout: 15000 });
  const tabbed = await page.$eval('.bcv-main > .bcv-screen', (e) => ({ tab: e.classList.contains('bcv-screen--tab'), slid: e.classList.contains('bcv-screen--fwd') || e.classList.contains('bcv-screen--back'), name: getComputedStyle(e).animationName }));
  check(tabbed.tab && !tabbed.slid && tabbed.name === 'bcv-fade-in', `a tab change fades in place — nothing slides between root tabs: ${JSON.stringify(tabbed)}`);
  check((await page.$eval('.bcv-tabbar__item.is-active', (e) => e.dataset.tab)) === 'courses' && (await texts('.bcv-ph-h1'))[0] === 'Courses', 'Courses tab lands with its large title');
  check(/(\d+ enrolled|\d+ of \d+ selected)$/.test((await texts('.bcv-ph-title__sub'))[0]), `subtitle counts the selection: ${(await texts('.bcv-ph-title__sub'))[0]}`);
  const selN = Number(((await texts('.bcv-ph-title__sub'))[0].match(/(\d+) (enrolled|of)/) || [])[1]);
  check(selN > 0 && (await page.$$('.bcv-ph-crow')).length === selN, `only the selected courses are listed (${(await page.$$('.bcv-ph-crow')).length} rows for ${selN} selected)`);
  check(await eventually(async () => (await texts('.bcv-ph-crow__sub')).some((t) => /\d+ of \d+ submitted/.test(t))), 'rows show submitted ÷ assigned');
  check((await texts('.bcv-ph-crow__pct')).every((t) => /^(\d+(\.\d+)?%|N\/A)$/.test(t)), `every row carries its current score or N/A: ${(await texts('.bcv-ph-crow__pct')).join(', ')}`);
  await shot('02-courses');
  // a nickname (Canvas's own): swipe a course row left for Nickname, a sheet with one field
  const crowCode = (await texts('.bcv-ph-crow__code'))[0];
  await swipeLeft('.bcv-ph-crow', 90);
  check(await eventually(() => page.$eval('.bcv-ph-crow', (e) => e.closest('.bcv-ph-swipe').classList.contains('is-open'))) && (await texts('.bcv-ph-swipe.is-open .bcv-ph-swipe__act')).join(',') === 'Nickname' && page.url() === `${BASE}/courses`, 'swiping a course row left reveals Nickname (and does not open the course)');
  await shot('02b-courses-swipe');
  await page.click('.bcv-ph-swipe.is-open .bcv-ph-swipe__act');
  await sheet();
  check((await texts('.bcv-ph-sheet__title'))[0] === 'Nickname' && (await page.$eval('.bcv-ph-nick__input', (e) => e.placeholder)) === crowCode && !(await page.$('.bcv-ph-sheet__actions .is-danger')), 'the Nickname sheet: the real name as the placeholder, nothing to remove yet');
  await page.fill('.bcv-ph-nick__input', 'Calc');
  await page.click('.bcv-ph-sheet__actions .bcv-ph-bigbtn.is-primary');
  check(await eventually(async () => !(await page.$('.bcv-sheet-ov')) && (await texts('.bcv-ph-crow__code'))[0] === 'Calc'), 'Save renames the row (the nickname is Canvas\'s own)');
  await swipeLeft('.bcv-ph-crow', 90);
  await eventually(() => page.$eval('.bcv-ph-crow', (e) => e.closest('.bcv-ph-swipe').classList.contains('is-open')));
  await page.click('.bcv-ph-swipe.is-open .bcv-ph-swipe__act');
  await sheet();
  await page.click('.bcv-ph-sheet__actions .bcv-ph-bigbtn.is-danger');
  check(await eventually(async () => !(await page.$('.bcv-sheet-ov')) && (await texts('.bcv-ph-crow__code'))[0] === crowCode), 'Remove nickname restores the real name');

  // ---- To Do -------------------------------------------------------------------------------------
  console.log('To Do');
  await tab('todo');
  await page.waitForSelector('.bcv-ph-progress', { timeout: 15000 });
  check((await page.$eval('.bcv-tabbar__item.is-active', (e) => e.dataset.tab)) === 'todo' && (await texts('.bcv-ph-h1'))[0] === 'To Do', 'To Do tab lands (a hash route, no page load)');
  check(/^\d+%$/.test((await texts('.bcv-ph-progress__pct'))[0]) && /\d+ of \d+ done/.test((await texts('.bcv-ph-progress__note'))[0]), `progress card: ${(await texts('.bcv-ph-progress__line'))[0]}`);
  check((await texts('.bcv-seg__btn, .bcv-seg button')).join(',') === 'Date,Priority,Course', 'group switch: Date / Priority / Course');
  check((await texts('.bcv-ph-switchrow__t'))[0] === 'Completed hidden' && (await texts('.bcv-ph-todo__add'))[0] === 'Add your own task', 'the completed switch and the "Add your own task" row above the list');
  check((await texts('.bcv-ph-ghead__t'))[0] === 'Overdue' && (await texts('.bcv-ph-ghead__t')).some((t) => /^(Today|Tomorrow|Next 7 days|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)$/.test(t)) && (await texts('.bcv-ph-row__time')).length > 0, `grouped by date with times, past due with nothing in first: ${(await texts('.bcv-ph-ghead__t')).join(', ')}`);
  check((await page.locator('.bcv-ph-trow', { hasText: 'W2 HW' }).first().locator('.bcv-status').allTextContents()).join() === 'Missing', 'an overdue row says Missing, the same word as the desktop');
  await shot('03-todo');
  // a tap opens the task sheet; a priority chosen there shows on the row
  const taskId = await page.$eval('.bcv-ph-trow', (e) => e.dataset.item);
  const taskTitle = (await texts(`.bcv-ph-trow[data-item="${taskId}"] .bcv-ph-row__title`))[0];
  await page.click(`.bcv-ph-trow[data-item="${taskId}"] .bcv-ph-row__body`);
  await sheet();
  check((await texts('.bcv-ph-tsheet__title'))[0] === taskTitle && (await texts('.bcv-ph-tsheet__pri')).join(',') === 'High,Medium,Low,None' && (await texts('.bcv-ph-sheet__actions .bcv-ph-bigbtn')).join(',').startsWith('Mark done,'), `the task sheet: "${taskTitle}", priority chips, Mark done / Open`);
  await shot('03b-todo-task');
  await page.click('.bcv-ph-tsheet__pri[data-pri="3"]');
  check(await eventually(async () => !(await page.$('.bcv-sheet-ov')) && (await texts(`.bcv-ph-trow[data-item="${taskId}"] .bcv-ph-pri`))[0] === 'High'), 'High closes the sheet and flags the row');
  await page.click('.bcv-seg button:nth-child(2)');
  check(await eventually(async () => (await texts('.bcv-ph-ghead__t'))[0] === 'High priority'), `Priority groups the flagged task first: ${(await texts('.bcv-ph-ghead__t')).join(', ')}`);
  await page.click('.bcv-seg button:nth-child(3)');
  check(await eventually(async () => (await texts('.bcv-ph-ghead__t')).every((t) => !/^(Today|Tomorrow|High priority)$/.test(t)) && (await texts('.bcv-ph-ghead__n')).some((t) => /item/.test(t))), `Course groups by course: ${(await texts('.bcv-ph-ghead__t')).join(', ')}`);
  await page.click('.bcv-seg button:nth-child(1)');
  await eventually(async () => (await page.$$(`.bcv-ph-trow[data-item="${taskId}"]`)).length === 1);
  // swipe left: Priority / Done; Done marks it and the row leaves the open list
  await swipeLeft(`.bcv-ph-trow[data-item="${taskId}"]`);
  check(await eventually(() => page.$eval(`.bcv-ph-trow[data-item="${taskId}"]`, (e) => e.closest('.bcv-ph-swipe').classList.contains('is-open'))) && (await texts('.bcv-ph-swipe.is-open .bcv-ph-swipe__act')).join(',') === 'Priority,Done', 'swiping a task left reveals Priority / Done');
  await shot('03c-todo-swipe');
  await page.click('.bcv-ph-swipe.is-open .bcv-ph-swipe__act:last-child');
  check(await eventually(async () => !(await page.$(`.bcv-ph-trow[data-item="${taskId}"]`))), 'Done marks the task complete and it leaves the open list');
  const phStill = await page.evaluate(() => ({ running: document.getAnimations().filter((a) => a.animationName === 'bcv-fade-up' && a.playState === 'running').length, entering: document.querySelectorAll('.bcv-ph-body .bcv-enter').length, rows: document.querySelectorAll('.bcv-ph-trow').length }));
  check(phStill.running === 0 && phStill.entering === 0 && phStill.rows > 0, `the list is drawn again in place after Done — no entrance runs, nothing fades back in (${JSON.stringify(phStill)})`);
  await page.click('.bcv-ph-switchrow .bcv-switch');
  check(await eventually(async () => (await texts('.bcv-ph-switchrow__t'))[0] === 'Showing completed' && (await page.$eval(`.bcv-ph-trow[data-item="${taskId}"]`, (e) => e.classList.contains('is-done')).catch(() => false))), 'Showing completed brings it back, done');
  await page.click(`.bcv-ph-trow[data-item="${taskId}"] .bcv-ph-circle`);
  await eventually(() => page.$eval(`.bcv-ph-trow[data-item="${taskId}"]`, (e) => !e.classList.contains('is-done')).catch(() => false));
  await page.click('.bcv-ph-switchrow .bcv-switch');
  await eventually(async () => (await texts('.bcv-ph-switchrow__t'))[0] === 'Completed hidden');
  await page.click(`.bcv-ph-trow[data-item="${taskId}"] .bcv-ph-row__body`);
  await sheet();
  await page.click('.bcv-ph-tsheet__pri[data-pri="0"]');
  await eventually(async () => !(await page.$('.bcv-sheet-ov')));
  // a task of your own: the composer, then the row, then its sheet's Delete
  await page.click('.bcv-ph-todo__add');
  await page.waitForSelector('.bcv-ph-composer', { timeout: 5000 });
  check((await page.$eval('.bcv-ph-composer__add', (e) => e.disabled)) === true && (await texts('.bcv-ph-composer__pri')).join(',') === 'High,Medium,Low,None' && /^Today · /.test((await texts('.bcv-ph-composer__date'))[0]) && !(await page.$('.bcv-ph-composer .bcv-seg')) && !(await page.$('input[type="date"]')), `the composer opens with Add disabled until a title is typed; the date field starts at today (${(await texts('.bcv-ph-composer__date'))[0]})`);
  await page.fill('.bcv-ph-composer__title', 'Return the library books');
  await page.click('.bcv-ph-composer__date');
  await page.waitForSelector('.bcv-datepop', { timeout: 3000 });
  await page.waitForFunction(() => { const e = document.querySelector('.bcv-datepop'); return !!e && e.getAnimations({ subtree: true }).every((a) => a.playState === 'finished'); }, null, { timeout: 3000 }).catch(() => {}); // it grows in from the field first
  const popBox = await page.$eval('.bcv-datepop', (e) => { const r = e.getBoundingClientRect(); return { l: Math.round(r.left), r: Math.round(window.innerWidth - r.right), b: Math.round(window.innerHeight - r.bottom), day: Math.round(e.querySelector('.bcv-datepop__day').getBoundingClientRect().height), clipped: e.scrollHeight > e.clientHeight + 1 }; });
  check((await page.$$('.bcv-datepop__day')).length === 42 && popBox.l === popBox.r && popBox.l >= 12 && popBox.l <= 24 && popBox.b > 60 && popBox.day >= 40 && !popBox.clipped && (await texts('.bcv-datepop__q')).join(',') === 'Today,Tomorrow,Next Monday', `the calendar rises above the tab bar, full width, with touch-sized days and the shortcuts in view (${JSON.stringify(popBox)})`);
  await shot('03e-todo-calendar');
  await page.click('.bcv-datepop__q:nth-child(2)'); // Tomorrow
  await page.waitForFunction(() => !document.querySelector('.bcv-datepop'), null, { timeout: 3000 });
  check(/^Tomorrow · /.test((await texts('.bcv-ph-composer__date'))[0]), `Tomorrow closes it and the field follows: ${(await texts('.bcv-ph-composer__date'))[0]}`);
  await page.click('.bcv-ph-composer__pri[data-pri="1"]');
  await page.click('.bcv-ph-composer__add');
  check(await eventually(async () => (await texts('.bcv-ph-trow .bcv-ph-row__title')).includes('Return the library books') && !(await page.$('.bcv-ph-composer'))), 'Add task creates a planner note that lands in the list');
  check((await texts('.bcv-ph-trow:has-text("Return the library books") .bcv-ph-row__sub'))[0] === 'My task · Personal' && (await texts('.bcv-ph-trow:has-text("Return the library books") .bcv-ph-pri'))[0] === 'Low', 'the row says My task · Personal with its Low flag');
  await shot('03d-todo-own-task');
  await page.click('.bcv-ph-trow:has-text("Return the library books") .bcv-ph-row__body');
  await sheet();
  check((await texts('.bcv-ph-sheet__actions .bcv-ph-bigbtn')).join(',') === 'Mark done,Delete task', 'a task of your own offers Delete instead of Open');
  page.once('dialog', (d) => d.accept());
  await page.click('.bcv-ph-sheet__actions .bcv-ph-bigbtn.is-danger');
  check(await eventually(async () => !(await texts('.bcv-ph-trow .bcv-ph-row__title')).includes('Return the library books')), 'Delete task removes it from the planner');

  // ---- Grades ------------------------------------------------------------------------------------
  console.log('Grades');
  await tab('gpa');
  await page.waitForSelector('.bcv-ph-hero', { timeout: 15000 });
  await rolled();
  check((await page.$eval('.bcv-tabbar__item.is-active', (e) => e.dataset.tab)) === 'gpa' && (await texts('.bcv-ph-h1'))[0] === 'Grades', 'Grades tab lands');
  check(/^\d\.\d\d$/.test((await texts('.bcv-ph-hero__gpa'))[0]) && /^goal \d\.\d\d$/.test((await texts('.bcv-ph-hero__goalnote'))[0]) && /(above|below) goal/.test((await texts('.bcv-ph-hero__diff'))[0]) && (await page.$eval('.bcv-ph-hero__gpa', (e) => getComputedStyle(e).color)) === 'rgb(255, 255, 255)', `GPA hero, white on indigo: ${(await texts('.bcv-ph-hero'))[0]}`);
  const cards = await page.$$('.bcv-ph-gcard');
  const ringFills = await page.$$eval('.bcv-ph-gcard', (els) => els.map((e) => e.querySelectorAll('.bcv-ph-ring svg circle').length));
  const scoredN = (await texts('.bcv-ph-gcard__pct')).filter((t) => /%$/.test(t)).length;
  check(cards.length > 0 && ringFills.every((n) => n >= 1) && ringFills.filter((n) => n === 2).length === scoredN && (await page.$$('.bcv-ph-gcard.is-open')).length === 0, `${cards.length} course cards with rings (a fill on the ${scoredN} scored), all folded`);
  check((await texts('.bcv-ph-gcard__sub')).every((t) => /\d+ of \d+ graded|nothing graded/.test(t)) && (await texts('.bcv-ph-gcard__letter')).some((t) => /^[A-F][+−]?$/.test(t)), `cards show graded counts and letters: ${(await texts('.bcv-ph-gcard__letter')).join(', ')}`);
  await shot('04-grades');
  // the goal stepper in the hero saves as it goes
  const before = (await texts('.bcv-ph-hero__goalv'))[0]; // 4.00, the top of the scale: so it steps down first
  await page.click('.bcv-ph-hero__step:first-child');
  const after = (await texts('.bcv-ph-hero__goalv'))[0];
  check(/^\d\.\d\d$/.test(before) && Math.abs(Number(before) - Number(after) - 0.05) < 0.001 && (await texts('.bcv-ph-hero__goalnote'))[0] === `goal ${after}`, `goal stepper: ${before} → ${after}, the note follows`);
  await page.click('.bcv-ph-hero__step:last-child');
  await eventually(async () => (await texts('.bcv-ph-hero__goalv'))[0] === before);
  // a ring expands on a tap, one at a time
  await page.click('.bcv-ph-gcard:nth-child(1) .bcv-ph-gcard__hd');
  check(await eventually(async () => (await page.$$('.bcv-ph-gcard.is-open')).length === 1 && (await page.$$('.bcv-ph-gcard.is-open .bcv-ph-ring__cat')).length > 0 && (await page.$$('.bcv-ph-gcard.is-open .bcv-ph-cat')).length > 0 && (await page.$$('.bcv-ph-gcard.is-open .bcv-ph-target__btn')).length > 3), 'tapping a card opens its category rings, the breakdown and the target picker');
  await shot('04b-grades-open');
  await page.click('.bcv-ph-gcard:nth-child(2) .bcv-ph-gcard__hd');
  check(await eventually(async () => (await page.$$('.bcv-ph-gcard.is-open')).length === 1 && (await page.$eval('.bcv-ph-gcard:nth-child(2)', (e) => e.classList.contains('is-open')))), 'opening another card folds the first (one open at a time)');
  const targetBefore = await page.$eval('.bcv-ph-gcard.is-open .bcv-ph-target__btn.is-on', (e) => e.textContent).catch(() => null);
  await page.click('.bcv-ph-gcard.is-open .bcv-ph-target__btn:first-child');
  check(await eventually(() => page.$eval('.bcv-ph-gcard.is-open .bcv-ph-target__btn.is-on', (e) => e.textContent === 'A+')), `the target picker saves a target (${targetBefore} → A+)`);
  // what-if: the switch shows the banner and a score field on the open card; nothing is saved
  await page.click('.bcv-ph-whatif .bcv-switch');
  check(await eventually(async () => await visible('.bcv-ph-warn') && !!(await page.$('.bcv-ph-gcard.is-open .bcv-ph-whatif__in'))), 'What-if shows the "not your actual score" banner and a score field on the open card');
  const gpaBefore = (await texts('.bcv-ph-hero__gpa'))[0];
  await page.fill('.bcv-ph-gcard.is-open .bcv-ph-whatif__in', '100');
  await page.press('.bcv-ph-gcard.is-open .bcv-ph-whatif__in', 'Tab');
  check(await eventually(async () => (await texts('.bcv-ph-gcard.is-open .bcv-ph-gcard__pct'))[0] === '100%' && !!(await page.$('.bcv-ph-gcard.is-open .bcv-ph-gcard__tried')) && /what-if/.test((await texts('.bcv-ph-hero__goalnote'))[0])), `a what-if score re-figures the card and the hero (${gpaBefore} → ${(await texts('.bcv-ph-hero__gpa'))[0]})`);
  await shot('04c-grades-whatif');
  await page.click('.bcv-ph-whatif .bcv-switch');
  check(await eventually(async () => !(await visible('.bcv-ph-warn')) && (await texts('.bcv-ph-hero__gpa'))[0] === gpaBefore), 'switching what-if off restores the real numbers');

  // ---- Calendar ----------------------------------------------------------------------------------
  console.log('Calendar');
  await tab('calendar');
  await page.waitForSelector('.bcv-ph-cal__grid', { timeout: 15000 });
  check((await page.$eval('.bcv-tabbar__item.is-active', (e) => e.dataset.tab)) === 'calendar' && /^[A-Z][a-z]+( \d{4})?$/.test((await texts('.bcv-ph-h1'))[0]), `Calendar tab lands on the month: ${(await texts('.bcv-ph-h1'))[0]}`);
  check((await page.$$('.bcv-ph-day')).length === 42 && (await page.$$('.bcv-ph-day.is-today')).length === 1 && (await page.$$('.bcv-ph-day__dots span')).length > 0, 'a six-week grid with today marked and event dots');
  check((await texts('.bcv-ph-ghead__n'))[0].startsWith('Today · '), `today's list under the grid: ${(await texts('.bcv-ph-ghead__t'))[0]}`);
  check(/^Calendars · \d+ of \d+ shown$/.test((await texts('.bcv-ph-linkrow'))[0]), `calendars row: ${(await texts('.bcv-ph-linkrow'))[0]}`);
  await shot('05-calendar');
  const dotted = await page.$('.bcv-ph-day:not(.is-off) .bcv-ph-day__dots span');
  if (dotted) {
    await page.evaluate((el) => el.closest('.bcv-ph-day').click(), dotted);
    check(await eventually(async () => (await page.$$('.bcv-ph-ev')).length > 0), 'tapping a dotted day lists its items');
    check((await texts('.bcv-ph-ev__sub')).every((t) => /· (Assignment|Quiz|Discussion|Event|Appointment)( ·|$)/.test(t)), `every row says its course and what it is, then the points or the place: ${(await texts('.bcv-ph-ev__sub')).slice(0, 3).join(' | ')}`);
  }
  // yesterday's W2 HW: past its due date with nothing in — not struck through as if done, but marked missing
  const yday = new Date(); yday.setDate(yday.getDate() - 1);
  if (yday.getMonth() === new Date().getMonth()) {
    await page.click(`.bcv-ph-day:not(.is-off)[aria-label$=" ${yday.getDate()}"]`);
    await eventually(async () => (await page.locator('.bcv-ph-ev', { hasText: 'W2 HW' }).count()) === 1);
    const w2 = page.locator('.bcv-ph-ev', { hasText: 'W2 HW' });
    check(!(await w2.evaluate((e) => e.classList.contains('is-done'))) && (await w2.locator('.bcv-status').allTextContents()).join() === 'Missing' && /PHYS 008 .* · Assignment · 15 pts$/.test((await w2.locator('.bcv-ph-ev__sub').textContent())), `missing work stays legible on the calendar and says Missing: ${(await w2.locator('.bcv-ph-ev__sub').textContent())}`);
  }
  await page.click('.bcv-ph-linkrow');
  await sheet();
  check((await page.$$('.bcv-ph-sheet .bcv-switch')).length > 0, 'the Calendars sheet lists every calendar with a switch');
  await shot('05b-calendar-sheet');
  await closeSheet();
  await page.click('.bcv-seg button:nth-child(3)');
  check(await eventually(async () => (await texts('.bcv-ph-h1'))[0] === 'Upcoming' && (await page.$$('.bcv-ph-ev')).length > 0), 'List view: three weeks of items by day');
  // an all-day event lands on the day Canvas names for it (all_day_date), not on the day its maker's midnight falls in this zone
  const plus4 = new Date(); plus4.setDate(plus4.getDate() + 4);
  const readingDay = await page.$$eval('.bcv-ph-body > div', (blocks) => { const b = blocks.find((x) => [...x.querySelectorAll('.bcv-ph-ev')].some((e) => /Reading day/.test(e.textContent))); return b ? { head: b.querySelector('.bcv-ph-ghead__t')?.textContent, time: [...b.querySelectorAll('.bcv-ph-ev')].find((e) => /Reading day/.test(e.textContent))?.querySelector('.bcv-ph-ev__time')?.textContent } : null; });
  check(readingDay?.head === `${['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'][plus4.getDay()]} ${plus4.getDate()}` && readingDay?.time === 'All day', `an all-day event made in another time zone sits on the day it names, marked All day: ${JSON.stringify(readingDay)}`);
  await shot('05c-calendar-list');
  await page.click('.bcv-seg button:nth-child(2)');
  await eventually(async () => (await page.$$('.bcv-ph-cal__grid')).length === 1);

  // ---- a course ----------------------------------------------------------------------------------
  console.log('course');
  await page.evaluate(() => sessionStorage.removeItem('bcv:trail')); // a fresh tab: nothing to come back from, so Back is the structural parent
  await page.goto(`${BASE}/courses/101`);
  await ready();
  await page.waitForSelector('.bcv-ph-body--course .bcv-ph-card', { timeout: 15000 });
  l = await layout();
  check(!l.root && l.topbarHidden === false && l.glass && !l.rail, `a pushed screen: glass top bar shown, the rail hidden (${JSON.stringify(l)})`);
  // a pushed screen slides in from the right on the phone spring (a root tab only fades: see the Courses tab above)
  const pushed = await page.$eval('.bcv-main > .bcv-screen', (e) => ({ fwd: e.classList.contains('bcv-screen--fwd'), name: getComputedStyle(e).animationName, ease: getComputedStyle(e).animationTimingFunction.slice(0, 7) }));
  check(pushed.fwd && pushed.name === 'bcv-push-in' && pushed.ease === 'linear(', `a pushed screen slides in from the right on the phone spring: ${JSON.stringify(pushed)}`);
  check(/^MATH-021-20 · Fall 2026/.test((await texts('.bcv-ph-head__sub'))[0]) && !(await visible('.bcv-head .bcv-reader-btn')) && !(await visible('.bcv-pill--term')), `course header: dot, name, one detail line (${(await texts('.bcv-ph-head__sub'))[0]})`);
  check((await texts('.bcv-topbar__title'))[0] === '' && (await page.$('.bcv-topbar__btn')) === null, 'the back bar carries no title under a large title, and no reader button');
  check((await texts('.bcv-topbar__back'))[0] === 'Courses', `back bar: ‹ ${(await texts('.bcv-topbar__back'))[0]}`);
  check((await page.$$('.bcv-ph-tab')).length === 0, 'Home carries no chip row: the course\'s tabs are the list below');
  const heads = await texts('.bcv-ph-body--course .bcv-ph-ghead__t');
  check(heads.includes('Open work') && heads.includes('Turned in'), `groups: ${heads.join(', ')}`);
  check((await texts('.bcv-ph-row--link .bcv-ph-row__title')).includes('Grades') && (await texts('.bcv-ph-row--link .bcv-ph-row__right')).some((t) => /%$/.test(t)), 'the course links list, Grades with the score');
  check((await raw('.bcv-ph-front .bcv-ph-kicker'))[0] === 'Front page' && (await texts('.bcv-ph-front .bcv-ph-btn'))[0] === 'Open', 'the front page folded with an Open button');
  check(await noOverflow(), 'no horizontal overflow on the course screen');
  await shot('06-course');
  await tapScreen('.bcv-ph-front .bcv-ph-btn');
  check(/\/courses\/101\/(pages\/|wiki|assignments\/syllabus)/.test(page.url()) && !(await page.$('.bcv-reader-ov')), `Open goes to the page itself, not a reader (${page.url().replace(BASE, '')})`);
  await page.goto(`${BASE}/courses/101`);
  await ready();
  await page.waitForSelector('.bcv-ph-row--link[data-tab="assignments"]', { timeout: 15000 });
  await tapScreen('.bcv-ph-row--link[data-tab="assignments"]');
  check((await page.$eval('.bcv-ph-tab.is-active', (e) => e.dataset.tab)) === 'assignments' && (await texts('.bcv-ph-tab'))[0] === 'Home', 'a link opens that tab, with the chip row for the rest');
  check((await texts('.bcv-topbar__back'))[0] === 'F26-MATH 021 20', `the tab's Back names the course it was opened from, as the Courses list names it: ‹ ${(await texts('.bcv-topbar__back'))[0]}`);
  await shot('06c-course-assignments');
  // the edge swipe pops the stack like Back
  await page.mouse.move(8, 500); await page.mouse.down(); await page.mouse.move(140, 505, { steps: 8 }); await page.mouse.up();
  const popped = await eventually(async () => page.url() === `${BASE}/courses/101` && !!(await page.$('.bcv-ph-body--course')));
  check(popped, `an edge swipe from the left pops back to the course${popped ? '' : ` (${page.url().replace(BASE, '')}, main: ${await page.evaluate(() => [...document.querySelectorAll('#bcv-main > *')].map((e) => e.className).join(' | '))}, sheet: ${await page.evaluate(() => !!document.querySelector('.bcv-sheet-ov'))}, root: ${await page.evaluate(() => document.documentElement.className)})`}`);
  check(await eventually(async () => (await texts('.bcv-topbar__back'))[0] === 'Courses'), `and the pop leaves the course's Back at Courses again (‹ ${(await texts('.bcv-topbar__back'))[0]})`);
  // an item opened from the Today tab says Back to Today, not to the list it belongs to
  await tab('dashboard');
  await page.waitForSelector('.bcv-ph-row[href*="/assignments/"]', { timeout: 15000 });
  await page.click('.bcv-ph-row[href*="/assignments/"] .bcv-ph-row__body'); // the preview first, then through it
  await page.waitForSelector('.bcv-ph-sheet--pv .bcv-ph-sheet__actions .bcv-ph-bigbtn', { timeout: 15000 });
  await tapScreen('.bcv-ph-sheet--pv .bcv-ph-sheet__actions .bcv-ph-bigbtn');
  await page.waitForSelector('.bcv-ph-item__title', { timeout: 15000 });
  check(await eventually(async () => (await texts('.bcv-topbar__back'))[0] === 'Today'), `an item opened from Today says Back to Today (‹ ${(await texts('.bcv-topbar__back'))[0]})`);
  await tapScreen('.bcv-topbar__back');
  check(await eventually(async () => page.url() === `${BASE}/` && !!(await page.$('.bcv-ph-stat'))), 'and Back goes to Today');

  // ---- an item with the submit block on the same page --------------------------------------------
  console.log('item');
  await page.evaluate(() => sessionStorage.removeItem('bcv:trail')); // a link straight into the item: Back falls back to its course
  await page.goto(`${BASE}/courses/104/assignments/4002`);
  await ready();
  await page.waitForSelector('.bcv-ph-item__title', { timeout: 15000 });
  check((await texts('.bcv-ph-item__title'))[0] === 'Week 2 Post Class Assignment: GC articles' && (await texts('.bcv-topbar__title'))[0] === 'Week 2 Post Class Assignment: GC articles', 'item title and the back bar title');
  check((await texts('.bcv-topbar__back'))[0] === 'Back', `with nothing to come back from, the back bar says Back (‹ ${(await texts('.bcv-topbar__back'))[0]})`);
  const itemFacts = (await texts('.bcv-ph-fact')).join(' · ');
  check(/^Due /.test(itemFacts) && /Points \d+/.test(itemFacts) && (await raw('.bcv-ph-instr .bcv-ph-kicker'))[0] === 'Instructions', `fact row and Instructions card: ${itemFacts}`);
  check(!(await page.$('.bcv-head--course')) && (await texts('.bcv-ph-chip--course'))[0] === 'F26-SPRK 010 103', 'an item page drops the course header; its course chip says where it is');
  check(!(await page.$('.bcv-ph-bigbtn.is-primary')) && !!(await page.$('.bcv-ph-body--item .bcv-sb--embed')) && (await raw('.bcv-sb--embed .bcv-sb__kicker'))[0] === 'Submit work', 'handing in lives on the same page: the submit block at the end, no separate Submit screen');
  check(/^[\w ]+ 11:59 PM 10 points Attempt 1 of unlimited$/.test((await texts('.bcv-sb--embed .bcv-sb__chips'))[0]), `the block's chips: ${(await texts('.bcv-sb--embed .bcv-sb__chips'))[0]} (no course chip, nothing stray)`);
  check((await page.$eval('.bcv-sb--embed .bcv-sb__btn--primary', (e) => e.disabled && e.textContent.trim() === 'Submit assignment' && e.getBoundingClientRect().height >= 48)), 'the full-width submit button waits until there is something to hand in');
  check(await noOverflow(), 'no horizontal overflow on the item page');
  await shot('07-item');
  await page.evaluate(() => document.querySelector('.bcv-sb--embed').scrollIntoView());
  await shot('08-item-submit');
  await page.click('.bcv-topbar__back');
  check(await eventually(async () => page.url() === `${BASE}/courses/104`), 'the back bar returns to the course');

  // ---- the rubric ---------------------------------------------------------------------------------
  // How the marks are decided belongs with the decision to hand work in, and with the mark itself:
  // a button in each place, one grid behind both. On a phone it is a sheet, never a card at the end
  // of a scroll nobody reaches.
  console.log('rubric');
  await page.goto(`${BASE}/courses/101/assignments/1009`);
  await ready();
  await page.waitForSelector('.bcv-sb--embed .bcv-sb__btn--primary', { timeout: 15000 });
  check(!(await page.$('.bcv-rubg')) && (await texts('.bcv-sb__foot .bcv-rubbtn'))[0] === 'Rubric', 'the rubric is a button beside Submit assignment, not a card down the page');
  await page.click('.bcv-sb__foot .bcv-rubbtn');
  await page.waitForSelector('.bcv-ph-sheet--rub .bcv-rubg__row', { timeout: 8000 });
  const sheetRub = await page.evaluate(() => {
    const row = document.querySelector('.bcv-rubg__row');
    return {
      title: document.querySelector('.bcv-ph-sheet__title')?.textContent,
      note: document.querySelector('.bcv-ph-sheet__note')?.textContent,
      rows: document.querySelectorAll('.bcv-rubg__row').length,
      cells: [...row.querySelectorAll('.bcv-rubg__cell')].map((e) => e.innerText.replace(/\s+/g, ' ').trim()),
      // narrow, the columns have nowhere to go: each criterion is its own block with its levels under it
      stacked: getComputedStyle(document.querySelector('.bcv-rubg')).display === 'block' && getComputedStyle(row).display === 'grid',
      headHidden: getComputedStyle(document.querySelector('.bcv-rubg__head')).display === 'none',
    };
  });
  check(sheetRub.title === 'Dis01 rubric' && /shown before you submit/.test(sheetRub.note || '') && sheetRub.rows === 2 && sheetRub.cells.join(' | ') === '6 Full marks | 3 Partial | 0 No marks' && sheetRub.stacked && sheetRub.headHidden, `the rubric stacks into blocks on a phone, one per criterion: ${JSON.stringify(sheetRub)}`);
  check(await noOverflow(), 'no horizontal overflow with the rubric open');
  await shot('08b-rubric');
  await closeSheet();
  // and a marked assignment offers the same grid from the grade itself
  await page.goto(`${BASE}/courses/104/assignments/4001`);
  await ready();
  await page.waitForSelector('.bcv-ph-grade', { timeout: 15000 });
  check((await texts('.bcv-ph-bigbtn.bcv-rubbtn'))[0] === 'See breakdown', 'a graded assignment offers See breakdown under the grade');
  // the mark sits in the title's own row, at its end, exactly as it does on the desktop
  const phMark = await page.evaluate(() => {
    const g = document.querySelector('.bcv-ph-grade'), t = document.querySelector('.bcv-ph-item__title');
    const gr = g.getBoundingClientRect(), tr = t.getBoundingClientRect(), hr = g.parentElement.getBoundingClientRect();
    return { tag: g.tagName, inHead: g.parentElement === t.parentElement, rightOfTitle: Math.round(gr.left - tr.right) >= 6, insetRight: Math.round(hr.right - gr.right) <= 1, score: g.querySelector('.bcv-ph-grade__score')?.textContent, banner: !!document.querySelector('.bcv-ph-banner') };
  });
  check(phMark.tag === 'BUTTON' && phMark.inHead && phMark.rightOfTitle && phMark.insetRight && phMark.score === '10' && !phMark.banner, `the mark is a chip in the title's row, and the banner it replaces is gone: ${JSON.stringify(phMark)}`);
  // the facts wrap into one row rather than a table, and no fact is invented where Canvas has none
  const phFacts = await page.$$eval('.bcv-ph-fact', (els) => els.map((e) => e.innerText.replace(/\s+/g, ' ').trim()));
  const webFacts = await page.evaluate(async (base) => { // the same five, from the same fields, as the desktop draws
    const r = await fetch(`${base}/api/v1/courses/104/assignments/4001?include[]=submission`, { credentials: 'same-origin' });
    const a = JSON.parse((await r.text()).replace(/^while\(1\);/, '')); // Canvas guards its JSON
    return { attempts: `${a.submission.attempt || 0} of ${a.allowed_attempts}`, points: String(a.points_possible), available: !!(a.unlock_at || a.lock_at) };
  }, BASE);
  check(phFacts.length === 4 && /^Due /.test(phFacts[0]) && phFacts[1] === `Points ${webFacts.points}` && phFacts[3] === `Attempts ${webFacts.attempts}` && !webFacts.available && !phFacts.some((t) => /^Available/.test(t)), `the phone draws the same facts, and none Canvas has no value for: ${phFacts.join(' | ')}`);
  await shot('08c-assignment-graded');
  // and the chip opens what is behind the mark: the feedback screen, the same shape as a quiz's,
  // in the course column rather than as a sheet
  await page.click('.bcv-ph-grade');
  await page.waitForSelector('.bcv-fb__scorecard', { timeout: 10000 });
  const phFb = await page.evaluate(() => ({
    url: location.search, sheet: !!document.querySelector('.bcv-sheet-ov'),
    score: document.querySelector('.bcv-fb__big').textContent,
    cards: [...document.querySelectorAll('.bcv-fb__q .bcv-fb__qn')].map((e) => e.textContent),
    files: [...document.querySelectorAll('.bcv-fb__file')].map((e) => e.innerText.replace(/\s+/g, ' ').trim()),
    reply: !!document.querySelector('.bcv-fb__input'),
    btns: [...document.querySelectorAll('.bcv-fb__btns .bcv-qz__big')].map((e) => e.textContent),
    fits: [...document.querySelectorAll('.bcv-fb__q, .bcv-fb__scorecard, .bcv-fb__file')].every((e) => e.getBoundingClientRect().right <= window.innerWidth + 1),
  }));
  check(/bcv=feedback/.test(phFb.url) && !phFb.sheet && phFb.score === '10 / 10' && phFb.cards.join(' | ') === 'Attempt 2 | Attempt 1' && phFb.reply, `the mark opens the feedback screen on a phone: ${JSON.stringify({ ...phFb, files: undefined, btns: undefined })}`);
  check(phFb.files.length === 1 && /Preview Download$/.test(phFb.files[0]) && phFb.btns.length === 3 && phFb.fits && await noOverflow(), `with the file both ways, the way back, and everything inside the screen: ${phFb.files[0]} · ${phFb.btns.join(' | ')}`);
  await shot('08d-feedback');
  await page.goto(`${BASE}/courses/104/assignments/4001`); // the rubric check below reads the page itself
  await ready();
  await page.waitForSelector('.bcv-ph-bigbtn.bcv-rubbtn', { timeout: 15000 });
  await page.click('.bcv-ph-bigbtn.bcv-rubbtn');
  await page.waitForSelector('.bcv-ph-sheet--rub .bcv-rubg__cell.is-got', { timeout: 8000 });
  const got = await page.$eval('.bcv-rubg__row', (e) => ({ got: e.querySelector('.bcv-rubg__cell.is-got')?.innerText.replace(/\s+/g, ' ').trim(), pts: e.querySelector('.bcv-rubg__ptsv')?.textContent }));
  check(/^3 Partial/.test(got.got || '') && got.pts === '4 / 6', `and it rings the level the work was given: ${JSON.stringify(got)}`);
  await closeSheet();

  // ---- Mark as done, and Previous / Next, on a phone -------------------------------------------
  await page.goto(`${BASE}/courses/101/assignments/1003`);
  await ready();
  await page.waitForSelector('.bcv-ph-done', { timeout: 15000 });
  const phDone = await page.$eval('.bcv-ph-done', (e) => ({ text: e.textContent.trim(), pressed: e.getAttribute('aria-pressed'), big: e.classList.contains('bcv-ph-bigbtn') }));
  check(phDone.text === 'Mark as done' && phDone.pressed === 'false' && phDone.big, `a must_mark_done assignment offers Mark as done as a big button: ${JSON.stringify(phDone)}`);
  await press('.bcv-ph-done');
  check(await eventually(async () => (await page.$eval('.bcv-ph-done', (e) => e.getAttribute('aria-pressed') === 'true' && e.textContent.trim() === 'Done'))), 'pressing it marks the item done and the button says so');
  await press('.bcv-ph-done'); // and back, so the desktop suite starts from the same state
  await eventually(async () => (await page.$eval('.bcv-ph-done', (e) => e.getAttribute('aria-pressed'))) === 'false');
  const phNav = await page.$$eval('.bcv-ph-nav .bcv-ph-navbtn', (els) => els.map((e) => `${e.querySelector('.bcv-ph-navkicker').textContent}: ${e.querySelector('.bcv-ph-navname').textContent}`));
  check(phNav.length >= 1 && phNav.every((x) => /^(Previous|Next): .+/.test(x)) && await noOverflow(), `Previous / Next sit at the end of the page: ${phNav.join(' | ')}`);
  await shot('08e-assignment-done-nav');
  await press('.bcv-ph-nav .bcv-ph-navbtn--next');
  // the old title is still on screen while the next page comes: wait for the new one by name
  const nextName = phNav.find((x) => x.startsWith('Next: ')).slice(6);
  const phNext = await page.waitForFunction((name) => !location.pathname.endsWith('/assignments/1003') && document.querySelector('.bcv-ph-item__title')?.textContent === name, nextName, { timeout: 10000 }).then(() => true).catch(() => false);
  check(phNext, `Next opens the next assignment: ${(await texts('.bcv-ph-item__title'))[0]}`);

  // ---- "Open in Canvas" ---------------------------------------------------------------------------
  // A phone has no address bar to type its way out of a Canvas page with, so the way back has to be
  // on screen and has to work: #bcv-app lets presses through to the page in the hole, and the back
  // bar and tab bar over it have to take them back.
  console.log('open in Canvas');
  await page.goto(`${BASE}/courses/104/assignments/4002?bcv=native`);
  await ready();
  await page.waitForSelector('html.bcv-punch #content', { timeout: 15000 });
  check((await visible('#bcv-topbar')) && (await visible('#bcv-tabbar')), 'the back bar and the tab bar stay over a Canvas page');
  const reach = await page.evaluate(() => {
    const hits = (el) => { if (!el) return false; const r = el.getBoundingClientRect(); const t = document.elementFromPoint(Math.round(r.left + r.width / 2), Math.round(r.top + r.height / 2)); return !!t && (t === el || el.contains(t)); };
    const bar = document.querySelector('#bcv-topbar').getBoundingClientRect();
    return { back: hits(document.querySelector('.bcv-topbar__back')), tab: hits(document.querySelector('.bcv-tabbar__item')), below: Math.round(document.querySelector('#main').getBoundingClientRect().top) >= Math.round(bar.bottom) - 1 };
  });
  check(reach.back && reach.tab && reach.below, `a press lands on the back bar and the tab bar rather than the Canvas page under them, and Canvas's page starts below the bar: ${JSON.stringify(reach)}`);
  check(await noOverflow(), 'no horizontal overflow on a Canvas page');
  await shot('09-open-in-canvas');
  await page.click('.bcv-topbar__back');
  check(await eventually(async () => !page.url().includes('bcv=native') && !(await page.$('html.bcv-punch'))), 'the back bar gets out of the Canvas page, back to the courses look');

  // ---- a quiz ------------------------------------------------------------------------------------
  console.log('quiz');
  await page.goto(`${BASE}/courses/101/quizzes/9011?bcv=take`);
  await page.waitForSelector('.bcv-qz__begin', { timeout: 15000 });
  check((await visible('#bcv-topbar')) && (await visible('#bcv-tabbar')) && !!(await page.$('.bcv-qz.is-embedded')), 'the quiz intro is a pushed course screen: the back bar and the tab bar stay');
  await shot('09-quiz-intro');
  await page.click('.bcv-qz__begin');
  await page.waitForSelector('.bcv-qz__opt', { timeout: 15000 });
  check((await page.$$('.bcv-qz__page--one, .bcv-qz__q')).length > 0 && await noOverflow(), 'one question at a time on a phone, no overflow');
  check(await eventually(async () => !(await visible('#bcv-tabbar')) && !(await visible('#bcv-topbar'))), 'the attempt takes the whole screen: the tab bar and the back bar go');
  const urlBefore = page.url();
  await page.mouse.move(8, 500); await page.mouse.down(); await page.mouse.move(140, 505, { steps: 8 }); await page.mouse.up();
  await new Promise((r) => setTimeout(r, 400));
  check(page.url() === urlBefore && !!(await page.$('.bcv-qz__opt')), 'the edge swipe is off during a quiz');
  await shot('09b-quiz-question');

  // ---- guided setup + the welcome on a phone -------------------------------------------------------
  console.log('guided setup');
  await page.goto(`${BASE}/?bcv=setup`);
  await page.waitForSelector('#bcv-setup .row', { timeout: 20000 });
  await page.waitForFunction(() => document.querySelector('#bcv-setup')?.shadowRoot.querySelector('.intro')?.hidden === true, null, { timeout: 8000 }); // the word-mark first
  await page.waitForTimeout(400);
  check((await page.$$('#bcv-setup .row.is-on')).length === 0 && (await page.$$('#bcv-setup .row[data-course]')).length >= 8 && (await page.$eval('#bcv-setup #next', (e) => e.disabled)) && (await page.$eval('#bcv-setup .fr', (e) => e.getBoundingClientRect().right <= window.innerWidth + 1 && e.getBoundingClientRect().left >= 0)) && !(await page.locator('#bcv-setup .rail').isVisible()) && (await page.$eval('#bcv-setup #stepLabel', (e) => e.textContent)) === '1 of 2' && await noOverflow(), 'the setup fits the phone screen without the rail and lists the courses, none ticked for you');
  check(await page.$eval('#bcv-setup .fr__blurb--strong', (e) => { const cs = getComputedStyle(e); return e.textContent === 'Only select the courses that count towards your GPA.' && parseInt(cs.fontWeight, 10) >= 700 && parseFloat(cs.fontSize) >= 16 && cs.color === 'rgb(10, 132, 255)'; }), 'the line about the GPA is big, bold and blue');
  await shot('11-setup');
  // start from none (Clear all), then pick five by hand
  if ((await page.$eval('#bcv-setup #selectAll', (e) => e.textContent)) === 'Select all') await page.click('#bcv-setup #selectAll');
  await page.click('#bcv-setup #selectAll');
  check(await eventually(async () => (await page.$$('#bcv-setup .row.is-on')).length === 0 && (await page.$eval('#bcv-setup #next', (e) => e.disabled))), 'Clear all unticks them all, and Continue waits for one');
  await page.$$eval('#bcv-setup .row[data-course]', (els) => els.slice(0, 5).forEach((e) => e.click())); // pick five
  // (Playwright selectors reach into the card's shadow root; document.querySelector would not)
  check(await eventually(async () => (await page.$$('#bcv-setup .row.is-on')).length === 5 && !(await page.$eval('#bcv-setup #next', (e) => e.disabled))), 'five picked: the rows tick and Continue comes alive');
  await page.click('#bcv-setup #next');
  await page.waitForSelector('#bcv-setup #track', { timeout: 10000 });
  check(await noOverflow() && (await page.$$('#bcv-setup .target')).length === 5, 'the grades step keeps to the screen with a target row per course');
  await shot('11b-setup-grades');
  // a phone has no sidebar and no dashboard views to choose between: the grades are the last step,
  // then the read-back, then Continue to appearance leads into Personalize
  check((await page.$eval('#bcv-setup #stepLabel', (e) => e.textContent)) === '2 of 2' && (await page.$eval('#bcv-setup #next', (e) => e.textContent.trim())) === 'Finish', 'a phone gets two steps');
  await page.click('#bcv-setup #next');
  await page.waitForSelector('#bcv-setup .summary__row', { timeout: 10000 });
  const readBack = await page.$$eval('#bcv-setup .summary__k', (els) => els.map((e) => e.textContent));
  check((await page.$eval('#bcv-setup #stepLabel', (e) => e.textContent)) === 'Ready' && readBack.join(' | ') === 'Courses shown | Grade history' && (await page.$eval('#bcv-setup #next', (e) => e.textContent.trim())) === 'Continue to appearance' && await noOverflow(), `then a read-back of those two answers alone, and Continue to appearance: ${readBack.join(' | ')}`);
  await shot('11c-setup-ready');
  await page.click('#bcv-setup #next');
  await page.waitForSelector('#bcv-setup .pz', { timeout: 20000 });
  await page.waitForTimeout(500);
  // Personalize on a phone: the look, the preview scaled to the screen, the themes; the photos and
  // the headers are the desktop's, so its steps are the colour and the courses' colours
  const phPz = await page.evaluate(() => { const r = document.querySelector('#bcv-setup').shadowRoot; const pv = r.querySelector('.pz__pv'); const w = window.innerWidth; return { looks: r.querySelectorAll('#pzLook .pz__segbtn').length, bars: r.querySelectorAll('#pzBars .pz__bar').length, h1: r.querySelector('.pz__h1').textContent, pvFits: pv.getBoundingClientRect().left >= -1 && pv.getBoundingClientRect().right <= w + 1, themes: r.querySelectorAll('#pzThemes .pz__sw').length, badges: r.querySelectorAll('.pz__badge').length, next: r.querySelector('#pzNext').textContent, fits: [...r.querySelectorAll('.pz__top, .pz__swatches, .pz__foot')].every((e) => e.getBoundingClientRect().right <= w + 1) && r.querySelector('.pz__foot').getBoundingClientRect().bottom <= window.innerHeight + 1 }; });
  check(phPz.looks === 3 && phPz.bars === 2 && phPz.h1 === 'Click any part of the preview to personalize' && phPz.pvFits && phPz.themes === 9 && phPz.badges === 0 && phPz.next === 'Continue' && phPz.fits && await noOverflow(), `Personalize on a phone: the look, two bars, the preview inside the screen, nine themes, no photo badges: ${JSON.stringify(phPz)}`);
  await page.click('#bcv-setup .pz__sw[data-theme="Pink"]');
  check(await eventually(async () => (await page.$eval('#bcv-setup .pz', (e) => e.style.getPropertyValue('--A'))) === '#ff375f'), 'Pink goes on the preview at once');
  await shot('11d-personalize');
  await page.click('#bcv-setup .pz__sw[data-theme="Regular"]'); // back to the regular colours: the pages after this read them
  await page.click('#bcv-setup #pzNext');
  await page.waitForSelector('#bcv-setup #pzCourses', { timeout: 10000 });
  const phColours = await page.evaluate(() => { const r = document.querySelector('#bcv-setup').shadowRoot; return { tabs: r.querySelectorAll('#pzCourses .pz__tab').length, swatches: r.querySelectorAll('#pzPal .pz__palsw').length, next: r.querySelector('#pzNext').textContent, fits: [...r.querySelectorAll('.pz__tabs, .pz__pal')].every((e) => e.getBoundingClientRect().right <= window.innerWidth + 1) }; });
  check(phColours.tabs === 5 && phColours.swatches === 16 && phColours.next === 'Save' && phColours.fits && await noOverflow(), `the courses' colours on a phone, Save at the end: ${JSON.stringify(phColours)}`);
  await shot('11e-personalize-courses');
  await page.click('#bcv-setup #pzNext'); // Save
  await page.waitForSelector('#bcv-setup #pzOpen', { timeout: 10000 });
  check((await page.$eval('#bcv-setup .pz__doneh1', (e) => e.textContent)) === 'Saved' && (await sw.evaluate(async () => (await self.BCV.settings.get()).appearance.theme?.name)) === 'Regular', 'Saved, with Regular kept');
  await Promise.all([page.waitForNavigation({ timeout: 20000 }), page.click('#bcv-setup #pzOpen')]); // Open Canvas: the page reloads
  // …and comes back black, with the welcome on it. A phone's header has no look switch to point at,
  // and Away Refresh is off unless turned on (2.98.13), so the welcome here is the Dashboard pointer alone
  await page.waitForSelector('#bcv-welcome[data-stage]', { timeout: 20000 });
  const welcomeAt = Date.now();
  const noContinueYet = (await page.$('.bcv-welcome__next:not([hidden])')) === null;
  const welcomeInfo = await page.$eval('#bcv-welcome', (e) => { const r = e.getBoundingClientRect(); return { stage: e.dataset.stage, bg: getComputedStyle(e).backgroundColor, full: r.width === innerWidth && r.height === innerHeight, look: !!e.querySelector('.bcv-welcome__look'), pill: !!e.querySelector('.bcv-welcome__away'), stats: e.querySelectorAll('.bcv-welcome__stat').length, sheet: !!e.querySelector('.bcv-welcome__sheetmock'), title: e.querySelector('.bcv-welcome__title')?.textContent }; });
  check((await page.$('#bcv-setup')) === null && page.url() === `${BASE}/` && /^(reload|navigate)$/.test(await page.evaluate(() => performance.getEntriesByType('navigation')[0]?.type)) && welcomeInfo.stage === 'peek' && welcomeInfo.bg === 'rgb(0, 0, 0)' && welcomeInfo.full && !welcomeInfo.look && !welcomeInfo.pill && welcomeInfo.stats === 3 && welcomeInfo.sheet && welcomeInfo.title === 'Click any of the dashboard cards to see more' && await noOverflow(), `Open Canvas reloads the page, which comes back black with the Dashboard pointer alone (no switch on a phone, no Away Refresh stage while it is off), no tour: ${JSON.stringify(welcomeInfo)}`);
  check(noContinueYet && await eventually(async () => (await page.$('.bcv-welcome__next:not([hidden])')) !== null, 7000) && Date.now() - welcomeAt >= TIMERS.welcomeWait - 500, 'Continue comes in only after the wait (three seconds shipped)');
  await page.waitForTimeout(400);
  await page.screenshot({ path: join(out, 'phone-12b-welcome-peek.png') });
  await page.click('.bcv-welcome__next');
  await page.waitForFunction(() => !document.querySelector('#bcv-welcome'), null, { timeout: 5000 });
  check(!(await page.$('html.bcv-welcome')) && (await sw.evaluate(async () => (await self.BCV.api.storage.local.get('welcome:pending'))['welcome:pending'])) === undefined && (await page.$('.bcv-ph-stats')) !== null, 'Continue takes the black away: Today, and the welcome does not come back');
  await page.click('.bcv-ph-avatar');
  await sheet();
  check((await texts('.bcv-ph-srow__label')).includes('Guided setup') && (await texts('.bcv-ph-srow__label')).includes('What’s new'), 'the account sheet offers the guided setup and What’s new');
  await closeSheet();

  // ---- Tools on a phone: under the avatar; the first press goes black, the words alone (no switch to drag to) ----
  console.log('tools');
  await sw.evaluate(() => self.BCV.api.storage.local.remove(['tools:welcomed', 'tools:decks']));
  await page.click('.bcv-ph-avatar');
  await sheet();
  check((await texts('.bcv-ph-srow__label')).includes('Tools'), 'the account sheet has a Tools row');
  await page.click('.bcv-ph-srow:has-text("Tools")');
  await page.waitForSelector('#bcv-welcome[data-stage="toolsIntro"]', { timeout: 20000 });
  const twAt = Date.now();
  check(page.url() === `${BASE}/#tools` && (await page.$eval('#bcv-welcome', (e) => getComputedStyle(e).backgroundColor)) === 'rgb(0, 0, 0)' && (await texts('.bcv-welcome__title'))[0] === 'Some helpful things' && (await texts('.bcv-welcome__hint'))[0] === 'Some tools to help you do more, quickly.' && await noOverflow(), 'the first press on Tools: black, the title and the gray line under it');
  check(await eventually(async () => (await page.$('.bcv-welcome__next:not([hidden])')) !== null, 7000) && Date.now() - twAt >= TIMERS.welcomeWait - 500, 'Continue comes in only after the wait (three seconds shipped)');
  await page.waitForTimeout(400);
  await page.screenshot({ path: join(out, 'phone-13-tools-welcome.png') });
  await page.click('.bcv-welcome__next');
  await page.waitForFunction(() => !document.querySelector('#bcv-welcome'), null, { timeout: 5000 });
  check((await page.$$('.bcv-tool-card')).length === 11 && (await texts('.bcv-topbar__title'))[0] === 'Tools' && await noOverflow() && (await page.$eval('#bcv-pins', (e) => getComputedStyle(e).display).catch(() => 'none')) === 'none' && (await sw.evaluate(async () => (await self.BCV.api.storage.local.get('tools:welcomed'))['tools:welcomed'])) === true, 'one Continue (a phone has no switch to drag to): the nine cards in one column, no pins, the welcome marked seen');
  await shot('13b-tools');
  await page.click('.bcv-tool-card[data-tool="fc"]');
  await page.waitForSelector('.bcv-tool[data-tool="fc"]', { timeout: 5000 });
  await page.click('.bcv-fc__new');
  await page.waitForSelector('.bcv-fc__name', { timeout: 3000 });
  check((await page.$eval('.bcv-tool', (e) => { const r = e.getBoundingClientRect(); return r.left >= 0 && r.right <= innerWidth + 1 && r.top >= 0 && r.bottom <= innerHeight + 1; })) && (await page.$eval('.bcv-fc__name', (e) => e.value)) === 'Untitled set' && (await texts('.bcv-tool__title'))[0] === 'Untitled set', 'a tool opens as a popup that fits the phone screen');
  await page.screenshot({ path: join(out, 'phone-13c-tools-deck.png') });
  await page.keyboard.press('Escape');
  await eventually(async () => !(await page.$('.bcv-tool-ov')));
  check((await page.$('.bcv-tool-ov')) === null && page.url() === `${BASE}/#tools`, 'Escape closes it and leaves the page where it was');

  // ---- what's new after an update, on a phone ---------------------------------------------------
  console.log("what's new");
  const whatsNew = new Function('self', `${readFileSync(join(extDir, 'content', 'app', 'whatsnew-notes.js'), 'utf8')}; return self.BCV_WHATS_NEW;`)({});
  const introDone = () => page.waitForFunction(() => document.querySelector('#bcv-whatsnew')?.shadowRoot.querySelector('.intro')?.hidden === true, null, { timeout: 8000 });
  const flag = async (key) => (await sw.evaluate((k) => self.BCV.api.storage.local.get(k), key))[key];
  await sw.evaluate(async () => { await self.BCV.api.storage.local.set({ 'whatsnew:from': '2.7.5' }); await self.BCV.api.storage.local.remove(['whatsnew:seen', 'welcome:appearance', 'themes:tried']); await self.BCV.settings.update({ appearance: { theme: { name: '' } } }); }); // (a theme not yet tried: the invitation is for those)
  await page.goto(`${BASE}/`);
  if (whatsNew[0].invite) {
    // this version puts an invitation in the notes' place: the four scenes across the phone's width
    // (the colour dots step aside), Not now and Personalize
    await page.waitForSelector('#bcv-whatsnew .inv', { timeout: 20000 });
    await introDone();
    await page.waitForTimeout(400);
    const inv = await page.evaluate(() => { const r = document.querySelector('#bcv-whatsnew').shadowRoot; const w = window.innerWidth; return { h1: r.querySelector('.fr__h1').textContent, scenes: [...r.querySelectorAll('.inv__scene')].filter((s) => s.getBoundingClientRect().width > 40).length, dots: getComputedStyle(r.querySelector('.inv__dots')).display, fits: [...r.querySelectorAll('.inv__strip, .fr__foot, .fr__h1')].every((e) => e.getBoundingClientRect().right <= w + 1), foot: [...r.querySelectorAll('.fr__foot button')].map((b) => b.textContent.trim()).join(','), notes: r.querySelectorAll('.wn__note').length }; });
    check(inv.h1 === 'Make it yours' && inv.scenes === 4 && inv.dots === 'none' && inv.fits && inv.foot === 'Not now,Personalize' && inv.notes === 0 && await noOverflow(), `the invitation fits the phone: four scenes across, no colour dots, Not now and Personalize, no notes: ${JSON.stringify(inv)}`);
    await shot('13-invite');
    await page.click('#bcv-whatsnew #later');
    await page.waitForFunction(() => !document.querySelector('#bcv-whatsnew'), null, { timeout: 5000 });
    check(!(await page.$('#bcv-whatsnew')) && (await flag('whatsnew:seen')) === manifest.version && (await flag('welcome:appearance')) === undefined, 'Not now closes it, the version marked seen, nothing armed');
    // Personalize opens the editor; its Open Canvas arms the pointer and reloads the page, which comes
    // back plain: a phone has no Appearance button to point at, so the pointer armed for it is dropped
    await sw.evaluate(async () => { await self.BCV.api.storage.local.set({ 'whatsnew:from': '2.12.0' }); await self.BCV.api.storage.local.remove('whatsnew:seen'); });
    await page.goto(`${BASE}/`);
    await page.waitForSelector('#bcv-whatsnew #personalize', { timeout: 20000 });
    await introDone();
    await page.click('#bcv-whatsnew #personalize');
    await page.waitForSelector('#bcv-setup .pz', { timeout: 20000 });
    check(!(await page.$('#bcv-whatsnew')) && !(await page.$('#bcv-welcome')) && (await flag('welcome:appearance')) === undefined, 'Personalize opens the editor over the page, nothing armed yet');
    for (let i = 0; i < 4 && !(await page.$('#bcv-setup #pzOpen')); i++) { await page.click('#bcv-setup #pzNext'); await page.waitForTimeout(400); }
    await page.waitForSelector('#bcv-setup #pzOpen', { timeout: 10000 });
    await Promise.all([page.waitForNavigation({ timeout: 20000 }), page.click('#bcv-setup #pzOpen')]);
    await page.waitForSelector('.bcv-ph-stat', { timeout: 20000 });
    await page.waitForTimeout(600);
    check(!(await page.$('#bcv-welcome')) && !(await page.$('#bcv-whatsnew')) && (await flag('welcome:appearance')) === undefined, 'the page after the editor comes back plain on a phone — no pointer, its flag dropped — and the invitation does not return');
    // the notes themselves wait behind Settings (and the account sheet's What's new row): this version alone
    await page.goto(`${BASE}/?bcv=whatsnew`);
    await page.waitForSelector('#bcv-whatsnew .wn__note', { timeout: 20000 });
    await introDone();
    check((await page.$eval('#bcv-whatsnew .wn__vnum', (e) => e.textContent)) === manifest.version && (await page.$('#bcv-whatsnew #earlier')) !== null && !/bcv=/.test(page.url()) && await noOverflow(), 'the notes open on their own from Settings, this version first, Earlier versions at their foot');
    await page.click('#bcv-whatsnew #dismiss');
    await page.waitForFunction(() => !document.querySelector('#bcv-whatsnew'), null, { timeout: 5000 });
    check(!(await page.$('#bcv-whatsnew')), 'Done closes them');
  } else {
    await page.waitForSelector('#bcv-whatsnew .wn__note', { timeout: 20000 });
    await introDone();
    await page.waitForTimeout(400);
    check((await page.$$('#bcv-whatsnew .wn__vh')).length > 1 && (await page.$eval('#bcv-whatsnew .wn__vnum', (e) => e.textContent)) === manifest.version && (await page.$('#bcv-whatsnew .wn__filter')) === null && (await page.$('#bcv-whatsnew #earlier')) !== null && await noOverflow(), 'what’s new fits the phone: one list, this version first, Earlier versions at its foot');
    await shot('13-whats-new');
    await page.click('#bcv-whatsnew #dismiss');
    await page.waitForFunction(() => !document.querySelector('#bcv-whatsnew'), null, { timeout: 5000 });
    check(!(await page.$('#bcv-whatsnew')) && (await flag('whatsnew:seen')) === manifest.version, 'Done closes it and marks the version seen');
  }

  check(errors.length === 0, `no page errors${errors.length ? `: ${errors.slice(0, 3).join(' | ')}` : ''}`);
} catch (e) {
  console.error('phone test crashed:', e?.stack || e);
  failures.push(`crash: ${e.message}`);
} finally {
  await context.close();
  server.kill();
  rmSync(extDir, { recursive: true, force: true });
  rmSync(userDataDir, { recursive: true, force: true });
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
console.log(`(${secs(Date.now() - startedAt)})`);
process.exit(failures.length ? 1 : 0);

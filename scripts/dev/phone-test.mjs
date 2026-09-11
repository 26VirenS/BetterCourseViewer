#!/usr/bin/env node
// The phone layout (the iPhone mockup): loads the extension into headless Chromium at a phone
// viewport against the mock Canvas server, walks the five tabs, a course, an item, the submit
// and quiz screens and the sheets, and saves screenshots to scripts/dev/out/phone-*.png.
// Requires Playwright (project or global install).
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { mkdirSync, cpSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';

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
const PORT = 8791;
const BASE = `http://localhost:${PORT}`;

// 1. temp copy of the extension whose content scripts also match localhost
const extDir = join(tmpdir(), `bcv-phone-ext-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) cs.matches.push(`${BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));

// 2. mock server
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'inherit' });
await new Promise((r) => setTimeout(r, 600));

const failures = [];
const check = (cond, label) => {
  console.log(`${cond ? '  ✓' : '  ✗'} ${label}`);
  if (!cond) failures.push(label);
};

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
  const setSettings = (patch) => sw.evaluate(async (p) => self.BCV.settings.update(p), patch);
  await sw.evaluate(() => self.BCV.api.storage.local.set({ 'setup:offered': true })); // the first-run setup is exercised on its own below

  const page = await context.newPage();
  // a page is ready to poke once it is drawn and nothing painted from the cache is still waiting on Canvas
  const __goto = page.goto.bind(page);
  page.gotoRaw = __goto;
  page.goto = async (...a) => { const r = await __goto(...a); await page.waitForFunction(() => { const c = document.documentElement.classList; return !c.contains('bcv-on') || c.contains('bcv-settled'); }, null, { timeout: 20000 }).catch(() => {}); return r; };
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
      await new Promise((r) => setTimeout(r, 150));
    }
    return false;
  };
  // tap something that lands a new screen (a page load or a hash change), and wait until it has drawn
  const tapScreen = async (sel) => {
    await page.evaluate(() => { const m = document.querySelector('#bcv-main > *'); if (m) m.dataset.old = '1'; });
    await page.click(sel);
    await page.waitForSelector('#bcv-main > *:not([data-old])', { timeout: 10000 });
    await page.waitForFunction(() => { const c = document.documentElement.classList; return !c.contains('bcv-on') || c.contains('bcv-settled'); }, null, { timeout: 20000 }).catch(() => {});
  };
  const tab = (id) => tapScreen(`.bcv-tabbar__item[data-tab="${id}"]`);
  const ready = () => page.waitForSelector('#bcv-app .bcv-tabbar__item', { timeout: 15000 });
  const sheet = () => page.waitForSelector('.bcv-sheet-ov .bcv-ph-sheet', { timeout: 5000 });
  const closeSheet = async () => { await page.keyboard.press('Escape'); await eventually(async () => !(await page.$('.bcv-sheet-ov'))); };
  const noOverflow = () => page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);
  const layout = () => page.evaluate(() => ({
    phone: document.documentElement.classList.contains('bcv-phone'),
    root: document.documentElement.classList.contains('bcv-ph-root'),
    tabbar: getComputedStyle(document.getElementById('bcv-tabbar')).position,
    topbarHidden: document.getElementById('bcv-topbar')?.hidden,
    side: !!document.querySelector('#bcv-side'),
    rail: !!document.querySelector('.bcv-rail') && getComputedStyle(document.querySelector('.bcv-rail')).display !== 'none',
  }));

  // ---- Today ------------------------------------------------------------------------------------
  console.log('Today');
  await page.goto(`${BASE}/`);
  await ready();
  await page.waitForSelector('.bcv-ph-stat', { timeout: 15000 });
  let l = await layout();
  check(l.phone && l.root && l.tabbar === 'fixed' && l.topbarHidden === true && !l.side, `phone layout: html.bcv-phone, root screen, fixed tab bar, no top bar, no sidebar (${JSON.stringify(l)})`);
  check((await texts('.bcv-tabbar__item')).join(',') === 'Today,Courses,To Do,Grades,Calendar', `five tabs: ${(await texts('.bcv-tabbar__item')).join(', ')}`);
  check((await page.$eval('.bcv-tabbar__item.is-active', (e) => e.dataset.tab)) === 'dashboard', 'Today is the active tab');
  check((await texts('.bcv-ph-h1'))[0] === 'Today' && /^(Sunday|Monday|Tuesday|Wednesday|Thursday|Friday|Saturday), \w+ \d+$/.test((await texts('.bcv-ph-title__sub'))[0]), `large title with the date line: ${(await texts('.bcv-ph-title__sub'))[0]}`);
  const stats = await texts('.bcv-ph-stat');
  check(stats.length === 3 && /^Due today \d+$/.test(stats[0]) && /^This week \d+$/.test(stats[1]) && /^Unread \d+$/.test(stats[2]), `three counters: ${stats.join(' | ')}`);
  check((await texts('.bcv-ph-ghead__t')).some((t) => /^(Today|Tonight|Next up)$/.test(t)) && (await page.$$('.bcv-ph-row')).length > 0, `the day's list: ${(await texts('.bcv-ph-ghead__t')).join(', ')}`);
  check((await raw('.bcv-ph-kicker')).includes('Week load') && (await page.$$('.bcv-ph-load__row')).length > 0, 'week load card with per-course bars');
  check(await visible('#bcv-fab'), 'the smart button floats above the tab bar');
  check(await noOverflow(), 'no horizontal overflow');
  await shot('01-today');

  await page.click('.bcv-ph-stat:nth-child(2)');
  await sheet();
  check((await texts('.bcv-ph-sheet__title'))[0] === 'Due this week' && (await page.$$('.bcv-ph-srow')).length > 0, 'a counter opens its list as a bottom sheet');
  await shot('01b-today-sheet');
  await closeSheet();
  check(!(await page.$('.bcv-sheet-ov')), 'Escape closes the sheet');

  // the circle marks a row done (a planner override) and the row stays
  const firstRow = (await texts('.bcv-ph-row__title'))[0];
  await page.click('.bcv-ph-row .bcv-ph-circle');
  check(await eventually(() => page.$eval('.bcv-ph-row', (e) => e.classList.contains('is-done'))), `the circle marks "${firstRow}" done`);
  await page.click('.bcv-ph-row .bcv-ph-circle');
  await eventually(() => page.$eval('.bcv-ph-row', (e) => !e.classList.contains('is-done')));

  // ---- account sheet + appearance -------------------------------------------------------------
  console.log('account sheet');
  await page.click('.bcv-ph-avatar');
  await sheet();
  const acct = await texts('.bcv-ph-srow__label');
  check(acct.join(',') === 'Inbox,Notifications,Groups,History,My Materials,Help,Dark appearance,Settings,Guided setup,Profile', `account sheet rows, with the school's own nav entries: ${acct.join(', ')} (no Sign out outside the app)`);
  check((await texts('.bcv-ph-srow__note'))[0] === 'No unread messages' || /unread message/.test((await texts('.bcv-ph-srow__note'))[0]), `Inbox row carries the unread count: ${(await texts('.bcv-ph-srow__note'))[0]}`);
  await shot('01c-account-sheet');
  await page.evaluate(() => { window.__bcvMarker = 1; });
  await page.click('.bcv-ph-srow:has-text("Dark appearance")');
  await page.waitForFunction(() => document.documentElement.getAttribute('data-bcv-theme') === 'dark' && window.__bcvMarker === undefined && document.querySelector('.bcv-ph-stat'), null, { timeout: 15000 });
  check(true, 'Dark appearance switches the theme and reloads the page (the web versions reload on an appearance change)');
  await ready();
  await page.waitForSelector('.bcv-ph-stat', { timeout: 15000 });
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
  check((await page.$eval('.bcv-tabbar__item.is-active', (e) => e.dataset.tab)) === 'courses' && (await texts('.bcv-ph-h1'))[0] === 'Courses', 'Courses tab lands with its large title');
  check(/\d+ courses? enrolled/.test((await texts('.bcv-ph-title__sub'))[0]), `subtitle counts the enrolment: ${(await texts('.bcv-ph-title__sub'))[0]}`);
  check(await eventually(async () => (await texts('.bcv-ph-crow__sub')).some((t) => /\d+ of \d+ submitted/.test(t))), 'rows show submitted ÷ assigned');
  check((await texts('.bcv-ph-crow__pct')).every((t) => /^(\d+(\.\d+)?%|N\/A)$/.test(t)), `every row carries its current score or N/A: ${(await texts('.bcv-ph-crow__pct')).join(', ')}`);
  const disclose = await page.$('.bcv-ph-disclose');
  if (disclose) {
    check(!(await visible('.bcv-ph-clist:nth-of-type(2)')), 'past courses fold away');
    await disclose.click();
    check(await eventually(() => page.$eval('.bcv-ph-disclose', (e) => e.getAttribute('aria-expanded') === 'true')), 'the disclosure opens them');
  }
  await shot('02-courses');

  // ---- To Do -------------------------------------------------------------------------------------
  console.log('To Do');
  await tab('todo');
  await page.waitForSelector('.bcv-ph-progress', { timeout: 15000 });
  check((await page.$eval('.bcv-tabbar__item.is-active', (e) => e.dataset.tab)) === 'todo' && (await texts('.bcv-ph-h1'))[0] === 'To Do', 'To Do tab lands (a hash route, no page load)');
  check(/^\d+%$/.test((await texts('.bcv-ph-progress__pct'))[0]) && /\d+ of \d+ done this week/.test((await texts('.bcv-ph-progress__note'))[0]), `progress card: ${(await texts('.bcv-ph-progress__line'))[0]}`);
  check((await texts('.bcv-seg__btn, .bcv-seg button')).join(',') === 'By date,By course', 'group switch');
  check((await texts('.bcv-ph-ghead__t')).some((t) => /^(Today|Tomorrow|Next 7 days)$/.test(t)) && (await texts('.bcv-ph-row__time')).length > 0, `grouped by date with times: ${(await texts('.bcv-ph-ghead__t')).join(', ')}`);
  await shot('03-todo');
  await page.click('.bcv-seg button:nth-child(2)');
  check(await eventually(async () => (await texts('.bcv-ph-ghead__t')).every((t) => !/^(Today|Tomorrow|Next 7 days)$/.test(t)) && (await texts('.bcv-ph-ghead__n')).some((t) => /open item/.test(t))), `By course groups by course: ${(await texts('.bcv-ph-ghead__t')).join(', ')}`);
  await page.click('.bcv-seg button:nth-child(1)');
  check((await texts('.bcv-ph-switchrow .bcv-ph-row__title'))[0] === 'Show completed', 'Show completed switch at the end');

  // ---- Grades ------------------------------------------------------------------------------------
  console.log('Grades');
  await tab('gpa');
  await page.waitForSelector('.bcv-ph-hero', { timeout: 15000 });
  check((await page.$eval('.bcv-tabbar__item.is-active', (e) => e.dataset.tab)) === 'gpa' && (await texts('.bcv-ph-h1'))[0] === 'Grades', 'Grades tab lands');
  check(/^\d\.\d\d$/.test((await texts('.bcv-ph-hero__gpa'))[0]) && /goal/.test((await texts('.bcv-ph-hero__note'))[0]) && (await page.$eval('.bcv-ph-hero__gpa', (e) => getComputedStyle(e).color)) === 'rgb(255, 255, 255)', `GPA hero, white on indigo: ${(await texts('.bcv-ph-hero'))[0]}`);
  const cards = await page.$$('.bcv-ph-gcard');
  check(cards.length > 0 && (await page.$$('.bcv-ph-gcard .bcv-ph-ring svg circle')).length === cards.length * 2, `${cards.length} course cards with rings`);
  check((await texts('.bcv-ph-gcard__sub')).every((t) => /\d+ of \d+ graded|nothing graded/.test(t)) && (await texts('.bcv-ph-gcard__letter')).some((t) => /^[A-F][+−]?$/.test(t)), `cards show graded counts and letters: ${(await texts('.bcv-ph-gcard__letter')).join(', ')}`);
  await shot('04-grades');
  await page.click('.bcv-ph-hero');
  await sheet();
  const before = (await texts('.bcv-ph-goal__val'))[0];
  await page.click('.bcv-ph-goal__step:last-child');
  const after = (await texts('.bcv-ph-goal__val'))[0];
  check(/^\d\.\d\d$/.test(before) && Math.abs(Number(after) - Number(before) - 0.05) < 0.001, `goal stepper: ${before} → ${after}`);
  await shot('04b-grades-goal');
  await page.click('.bcv-ph-bigbtn.is-primary');
  check(await eventually(async () => !(await page.$('.bcv-sheet-ov')) && new RegExp(after.replace('.', '\\.')).test((await texts('.bcv-ph-hero__note'))[0])), `Done saves the goal into the hero: ${(await texts('.bcv-ph-hero__note'))[0]}`);
  await page.click('.bcv-ph-hero');
  await sheet();
  await page.click('.bcv-ph-goal__step:first-child');
  await page.click('.bcv-ph-bigbtn.is-primary');
  await eventually(async () => !(await page.$('.bcv-sheet-ov')));

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
  }
  await page.click('.bcv-ph-linkrow');
  await sheet();
  check((await page.$$('.bcv-ph-sheet .bcv-switch')).length > 0, 'the Calendars sheet lists every calendar with a switch');
  await shot('05b-calendar-sheet');
  await closeSheet();
  await page.click('.bcv-seg button:nth-child(3)');
  check(await eventually(async () => (await texts('.bcv-ph-h1'))[0] === 'Upcoming' && (await page.$$('.bcv-ph-ev')).length > 0), 'List view: three weeks of items by day');
  await shot('05c-calendar-list');
  await page.click('.bcv-seg button:nth-child(2)');
  await eventually(async () => (await page.$$('.bcv-ph-cal__grid')).length === 1);

  // ---- a course ----------------------------------------------------------------------------------
  console.log('course');
  await page.goto(`${BASE}/courses/101`);
  await ready();
  await page.waitForSelector('.bcv-ph-body--course .bcv-ph-card', { timeout: 15000 });
  l = await layout();
  check(!l.root && l.topbarHidden === false && !l.rail, `a pushed screen: top bar shown, the rail hidden (${JSON.stringify(l)})`);
  check(/^MATH-021-20 · Fall 2026/.test((await texts('.bcv-ph-head__sub'))[0]) && !(await visible('.bcv-head .bcv-reader-btn')) && !(await visible('.bcv-pill--term')), `course header: dot, name, one detail line, chips (${(await texts('.bcv-ph-head__sub'))[0]})`);
  check((await texts('.bcv-topbar__title'))[0] === '' && (await page.$('.bcv-topbar__btn[title="Immersive Reader"]')) !== null, 'the back bar carries no title under a large title, and the reader button');
  check((await texts('.bcv-topbar__back'))[0] === 'Courses', `back bar: ‹ ${(await texts('.bcv-topbar__back'))[0]}`);
  const chips = await texts('.bcv-ph-tab');
  check(chips.length > 3 && chips[0] === 'Home' && (await page.$eval('.bcv-ph-tab.is-active', (e) => e.dataset.tab)) === 'home', `tab chips: ${chips.join(' · ')}`);
  check((await texts('.bcv-ph-next .bcv-ph-next__title'))[0]?.length > 0 && (await texts('.bcv-ph-next__btns .bcv-ph-btn')).includes('Open'), `Next up card: ${(await texts('.bcv-ph-next__title'))[0]}`);
  const heads = await texts('.bcv-ph-body--course .bcv-ph-ghead__t');
  check(heads.includes('Open work') && heads.includes('Turned in'), `groups: ${heads.join(', ')}`);
  check((await texts('.bcv-ph-row--link .bcv-ph-row__title')).includes('Grades') && (await texts('.bcv-ph-row--link .bcv-ph-row__right')).some((t) => /%$/.test(t)), 'the course links list, Grades with the score');
  check((await raw('.bcv-ph-front .bcv-ph-kicker'))[0] === 'Front page' && (await texts('.bcv-ph-front .bcv-ph-btn'))[0] === 'Read', 'the front page folded with a Read button');
  check(await noOverflow(), 'no horizontal overflow on the course screen');
  await shot('06-course');
  await page.click('.bcv-ph-front .bcv-ph-btn');
  check(await eventually(async () => !!(await page.$('.bcv-reader-ov'))), 'Read opens the reader');
  await shot('06b-course-reader');
  await page.evaluate(() => document.querySelector('.bcv-reader-ov')?.remove());
  await page.click('.bcv-topbar__btn[title="Immersive Reader"]');
  check(await eventually(async () => !!(await page.$('.bcv-reader-ov'))), 'so does the reader button in the back bar');
  await page.evaluate(() => document.querySelector('.bcv-reader-ov')?.remove());
  await tapScreen('.bcv-ph-tab[data-tab="assignments"]');
  check((await page.$eval('.bcv-ph-tab.is-active', (e) => e.dataset.tab)) === 'assignments' && (await texts('.bcv-topbar__back'))[0] === 'Back', 'a chip opens that tab with the chip row kept');
  await shot('06c-course-assignments');

  // ---- an item + submit -------------------------------------------------------------------------
  console.log('item');
  await page.goto(`${BASE}/courses/104/assignments/4002`);
  await ready();
  await page.waitForSelector('.bcv-ph-item__title', { timeout: 15000 });
  check((await texts('.bcv-ph-item__title'))[0] === 'Week 2 Post Class Assignment: GC articles' && (await texts('.bcv-topbar__title'))[0] === 'Week 2 Post Class Assignment: GC articles', 'item title and the back bar title');
  check(/Due .* · \d+ points/.test((await texts('.bcv-ph-item__meta'))[0]) && (await raw('.bcv-ph-instr .bcv-ph-kicker'))[0] === 'Instructions', `meta line and Instructions card: ${(await texts('.bcv-ph-item__meta'))[0]}`);
  check((await texts('.bcv-ph-bigbtn.is-primary'))[0] === 'Submit assignment' && (await page.$eval('.bcv-ph-bigbtn.is-primary', (e) => getComputedStyle(e).color)) === 'rgb(255, 255, 255)', 'the big primary button, white on blue');
  check(!(await page.$('.bcv-head--course')) && (await texts('.bcv-ph-chip--course'))[0] === 'F26-SPRK 010 103', 'an item page drops the course header; its course chip says where it is');
  await shot('07-item');
  await tapScreen('.bcv-ph-bigbtn.is-primary');
  check(page.url() === `${BASE}/courses/104/assignments/4002?bcv=submit` && (await texts('.bcv-sb__h1'))[0] === 'Week 2 Post Class Assignment: GC articles', 'Submit opens the submit screen');
  check(await noOverflow(), 'no horizontal overflow on the submit screen');
  await shot('08-submit');
  await page.click('.bcv-topbar__back');
  await page.waitForSelector('.bcv-ph-item__title', { timeout: 15000 });
  check(page.url() === `${BASE}/courses/104/assignments/4002`, 'the back bar returns to the item');

  // ---- a quiz ------------------------------------------------------------------------------------
  console.log('quiz');
  await page.goto(`${BASE}/courses/101/quizzes/9011?bcv=take`);
  await page.waitForSelector('.bcv-qz__begin', { timeout: 15000 });
  check(!(await visible('#bcv-tabbar')) && !(await visible('#bcv-topbar')) && !(await visible('#bcv-fab')), 'the quiz flow hides the tab bar, the top bar and the smart button');
  await shot('09-quiz-intro');
  await page.click('.bcv-qz__begin');
  await page.waitForSelector('.bcv-qz__opt', { timeout: 15000 });
  check((await page.$$('.bcv-qz__page--one, .bcv-qz__q')).length > 0 && await noOverflow(), 'one question at a time on a phone, no overflow');
  await shot('09b-quiz-question');

  // ---- the smart panel --------------------------------------------------------------------------
  console.log('smart panel');
  await page.goto(`${BASE}/`);
  await ready();
  await page.waitForSelector('.bcv-ph-stat', { timeout: 15000 });
  await page.click('#bcv-fab');
  await page.waitForSelector('#bcv-smart', { timeout: 5000 });
  const smartBox = await page.$eval('#bcv-smart', (e) => { const r = e.getBoundingClientRect(); return { w: Math.round(r.width), l: Math.round(r.left), b: Math.round(window.innerHeight - r.bottom) }; });
  check(smartBox.w >= 380 && smartBox.l <= 12, `the smart panel fills the width as a sheet (${JSON.stringify(smartBox)})`);
  await shot('10-smart');
  await page.keyboard.press('Escape');

  // ---- guided setup + the tour on a phone -------------------------------------------------------
  console.log('guided setup');
  await page.goto(`${BASE}/?bcv=setup`);
  await page.waitForSelector('#bcv-setup .row', { timeout: 20000 });
  await page.waitForTimeout(400);
  check((await page.$$('#bcv-setup .row.is-on')).length === 5 && (await page.$eval('#bcv-setup .card', (e) => e.getBoundingClientRect().right <= window.innerWidth + 1 && e.getBoundingClientRect().left >= 0)) && await noOverflow(), 'the setup card fits the phone screen and lists the favourites checked');
  await shot('11-setup');
  await page.click('#bcv-setup #next');
  await page.waitForSelector('#bcv-setup #track', { timeout: 10000 });
  check(await noOverflow() && (await page.$$('#bcv-setup .target')).length === 5, 'the grades step keeps to the screen with a target row per course');
  await shot('11b-setup-grades');
  await page.click('#bcv-setup #next');
  await page.waitForSelector('#bcv-setup .prov', { timeout: 10000 });
  await page.click('#bcv-setup #notNow');
  await page.waitForSelector('.bcv-tour__card', { timeout: 20000 });
  check((await page.$('#bcv-setup')) === null && page.url() === `${BASE}/`, 'Not now closes the card and the tour starts on Today');
  const overStats = await eventually(() => page.evaluate(() => { const r = document.querySelector('.bcv-tour__ring').getBoundingClientRect(); const t = document.querySelector('.bcv-ph-stats').getBoundingClientRect(); return r.width > 0 && r.left <= t.left + 1 && r.top <= t.top + 1 && r.right >= t.right - 1 && r.bottom >= t.bottom - 1; }).catch(() => false), 3000);
  check((await texts('.bcv-tour__title'))[0] === 'Your day at a glance' && overStats, 'the tour spotlights the phone counters');
  await page.click('.bcv-tour__btn.is-primary');
  await page.waitForFunction(() => document.querySelector('.bcv-tour__title')?.textContent.trim() === 'Everything in one place', null, { timeout: 10000 });
  check(/Inbox, Groups/.test((await texts('.bcv-tour__text'))[0]), 'the phone stop explains the tab bar and the avatar');
  await shot('12-tour');
  await page.click('.bcv-tour__x');
  await page.waitForFunction(() => !document.querySelector('.bcv-tour'), null, { timeout: 5000 });
  await page.click('.bcv-ph-avatar');
  await sheet();
  check((await texts('.bcv-ph-srow__label')).includes('Guided setup'), 'the account sheet offers the guided setup');
  await closeSheet();

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
process.exit(failures.length ? 1 : 0);

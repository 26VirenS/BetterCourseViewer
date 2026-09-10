#!/usr/bin/env node
// Loads the extension into headless Chromium against the mock Canvas server,
// walks every screen of the redesigned interface and saves screenshots to
// scripts/dev/out/. Requires Playwright (project or global install).
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
const PORT = 8787;
const BASE = `http://localhost:${PORT}`;

// 1. temp copy of the extension whose content scripts also match localhost
const extDir = join(tmpdir(), `bcv-ext-${Date.now()}`);
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
const shot = (page, name) => page.screenshot({ path: join(out, `${name}.png`) });

const userDataDir = join(tmpdir(), `bcv-profile-${Date.now()}`);
const context = await chromium.launchPersistentContext(userDataDir, {
  channel: 'chromium',
  headless: true,
  viewport: { width: 1400, height: 900 },
  args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`],
});
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  const extId = new URL(sw.url()).host;
  console.log('extension id', extId);
  const setSettings = (patch) => sw.evaluate(async (p) => self.BCV.settings.update(p), patch);

  const page = await context.newPage();
  page.on('pageerror', (e) => console.log('  page error:', e.message));
  page.on('console', (m) => { if (m.type() === 'error') console.log('  console:', m.text()); });
  const texts = (sel) => page.$$eval(sel, (els) => els.map((e) => (e.innerText || e.textContent).replace(/\s+/g, ' ').trim()));
  const visible = (sel) => page.$eval(sel, (el) => getComputedStyle(el).display !== 'none').catch(() => false);
  const waitText = (sel, re) => page.waitForFunction(([s, r]) => [...document.querySelectorAll(s)].some((e) => new RegExp(r).test(e.textContent)), [sel, re.source], { timeout: 10000 });
  // click something that re-renders the main screen, and wait until the old screen element is gone
  const clickScreen = async (sel) => {
    await page.evaluate(() => { const m = document.querySelector('#bcv-main > *'); if (m) m.dataset.old = '1'; });
    await page.click(sel);
    await page.waitForSelector('#bcv-main > *:not([data-old])', { timeout: 10000 });
  };
  const tab = (id) => clickScreen(`.bcv-rail [data-tab="${id}"]`);
  const nav = (id) => clickScreen(`.bcv-nav__item[data-nav="${id}"]`);

  // ---- dashboard --------------------------------------------------------------------
  console.log('dashboard');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  check(await visible('#bcv-app'), 'app shell visible');
  check(!(await visible('#application')), 'stock Canvas hidden');
  check(await page.$('#bcv-skin.is-on'), 'skin switch at the top-left is on');
  await waitText('.bcv-brand__sub', /Example University · Fall 2026/);
  check((await texts('.bcv-brand__name'))[0] === 'Localhost', `brand row: ${(await texts('.bcv-brand'))[0]}`);
  check(await page.$('.bcv-brand__tile img'), 'brand tile shows the school logo from Canvas');
  const navItems = await texts('.bcv-nav__item');
  check(navItems.length === 6 && navItems[0].startsWith('Dashboard') && navItems[5].startsWith('Inbox'), `sidebar nav: ${navItems.join(' | ')}`);
  await waitText('.bcv-nav__item[data-nav="todo"] .bcv-nav__count', /\d/);
  const todoCount = Number((await texts('.bcv-nav__item[data-nav="todo"] .bcv-nav__count'))[0]);
  check(todoCount >= 8, `To Do count in nav = ${todoCount}`);
  check((await texts('.bcv-nav__item[data-nav="inbox"] .bcv-nav__count'))[0] === '1', 'Inbox unread count in nav');
  await page.waitForFunction(() => document.querySelectorAll('.bcv-fav').length === 5, null, { timeout: 10000 });
  check((await texts('.bcv-fav')).length === 5, 'five favourite courses in the sidebar');
  check((await texts('.bcv-account__name'))[0] === 'Sam Student', 'account card shows the user');
  await page.waitForSelector('.bcv-stat', { timeout: 10000 });
  await waitText('.bcv-stat__value', /^\d+$/);
  const stats = await texts('.bcv-stat');
  check(stats.length === 3 && /Due today\s*4\s*35 points total/i.test(stats[0]) && /Due this week/i.test(stats[1]) && /Unread announcements/i.test(stats[2]), `stat cards: ${stats.join(' | ')}`);
  await waitText('.bcv-stats > :nth-child(3) .bcv-stat__value', /^3$/);
  const work = await texts('.bcv-work__row');
  check(work.length === 5 && /F26-MATH 021 20/.test(work[0]) && /\d+ \/ \d+/.test(work[0]), `workload rows: ${work[0]}`);
  check(!(await page.$('.bcv-work__more')), 'no disclosure when every course has work this week');
  const dayHeads = await texts('.bcv-day__head');
  check(dayHeads[0].startsWith('Today') && dayHeads[1].startsWith('Tomorrow'), `list view day groups: ${dayHeads.slice(0, 3).join(' | ')}`);
  const listRows = await texts('.bcv-day .bcv-row');
  check(listRows.some((t) => /Dis01.*10 pts.*Due 11:59 PM/.test(t)) && listRows.some((t) => /Quiz.*Qz01/.test(t)), 'list rows show course · kind, points and due time');
  check(listRows.some((t) => /to-do date/.test(t)), 'to-do-dated items are labelled, not shown as due');
  await shot(page, '01-dashboard-list');
  // mark one done
  const rowsBefore = (await page.$$('.bcv-day .bcv-row')).length;
  await page.click('.bcv-day .bcv-row .bcv-circle');
  await page.waitForSelector('.bcv-day .bcv-row.bcv-row--done', { timeout: 5000 });
  await page.waitForFunction((n) => Number(document.querySelector('.bcv-nav__item[data-nav="todo"] .bcv-nav__count').textContent) === n - 1, todoCount, { timeout: 5000 });
  check((await page.$$('.bcv-day .bcv-row')).length === rowsBefore, 'marking an item done keeps it in the list, ticked (planner override)');
  await page.click('.bcv-day .bcv-row.bcv-row--done .bcv-circle');
  await page.waitForFunction(() => !document.querySelector('.bcv-day .bcv-row--done'), null, { timeout: 5000 });
  check(true, 'ticking again marks it not done');
  await page.click('.bcv-day .bcv-row .bcv-circle');
  await page.waitForSelector('.bcv-day .bcv-row.bcv-row--done', { timeout: 5000 });
  // cards view
  await page.click('.bcv-seg__btn[data-value="cards"]');
  await page.waitForSelector('.bcv-ccard', { timeout: 5000 });
  await page.waitForFunction(() => document.querySelectorAll('.bcv-ccard .bcv-bar').length >= 5, null, { timeout: 10000 });
  const cards = await texts('.bcv-ccard');
  check(cards.length === 5 && /F26-MATH 021 20/.test(cards[0]) && /of \d+ items submitted/.test(cards[0]) && /due today/.test(cards[0]), `course cards: ${cards[0]}`);
  check((await page.$$('.bcv-ccard__quick')).length === 20, 'quick links on every card');
  await shot(page, '02-dashboard-cards');
  await page.click('.bcv-seg__btn[data-value="activity"]');
  await page.waitForSelector('.bcv-act__title', { timeout: 5000 });
  const acts = await texts('.bcv-act__kind');
  check(acts.length === 5 && /Announcement · F26-MATH 021 20/.test(acts[0]) && /Discussion · 23 replies/.test(acts[1]), `recent activity: ${acts.slice(0, 2).join(' | ')}`);
  await shot(page, '03-dashboard-activity');
  check(await sw.evaluate(async () => (await fetch('http://localhost:8787/dashboard/view').then((r) => r.text())).includes('activity')), 'dashboard view persisted to Canvas');
  await page.click('.bcv-seg__btn[data-value="list"]');

  // ---- courses ----------------------------------------------------------------------------
  console.log('courses');
  await nav('courses');
  await page.waitForSelector('.bcv-ccard__hero--term', { timeout: 10000 });
  check(page.url() === `${BASE}/courses`, 'sidebar navigation loads the real Canvas page');
  const termCards = await texts('.bcv-ccard');
  check(termCards.length === 5 && /Fall 2026/.test(termCards[0]) && /Enrolled as Student/.test(termCards[0]), `favourite course cards: ${termCards.length}`);
  const groupLabels = await texts('.bcv-body .bcv-group__head--reorder .bcv-label');
  check(groupLabels.some((l) => /collaboration team/i.test(l)), `term groups: ${groupLabels.join(', ')}`);
  const terms = await page.$$eval('[data-term]', (els) => els.map((e) => e.dataset.term));
  await page.click(`[data-term="${terms[0]}"] .bcv-reorder__down`);
  await page.waitForFunction((first) => document.querySelector('[data-term]').dataset.term !== first, terms[0], { timeout: 5000 });
  check((await page.$$eval('[data-term]', (els) => els.map((e) => e.dataset.term)))[1] === terms[0], 'term groups can be moved down (order saved)');
  await page.click(`[data-term="${terms[0]}"] .bcv-reorder__up`);
  await page.waitForFunction((first) => document.querySelector('[data-term]').dataset.term === first, terms[0], { timeout: 5000 });
  check((await texts('.bcv-body .bcv-row')).some((t) => /Placement Exam: Chemistry.*No nickname.*Student/.test(t)), 'non-favourite rows with role badge');
  check((await page.$$eval('[data-term]', (els) => els.map((e) => e.dataset.term)))[0] === 'Fall 2026', 'the current term (most dashboard courses) is listed first');
  await shot(page, '04-courses');
  await page.click('.bcv-seg__btn[data-value="past"]');
  check((await texts('.bcv-body .bcv-row')).some((t) => /S26-CSE 022 01/.test(t)), 'Past filter shows the completed course');
  await page.click('.bcv-seg__btn[data-value="all"]');
  await page.fill('.bcv-search input', 'phys');
  await page.waitForTimeout(100);
  check((await page.$$('.bcv-ccard')).length === 2, 'search filters courses');
  // star a course
  await page.fill('.bcv-search input', '');
  await page.waitForTimeout(100);
  const starRow = (await page.$$('.bcv-body .bcv-row')).find(async () => true);
  await starRow.$eval('.bcv-ccard__star', (b) => b.click());
  await page.waitForFunction(() => document.querySelectorAll('.bcv-ccard').length === 6, null, { timeout: 5000 });
  check(true, 'starring a course adds it to the dashboard (favourites API)');
  await page.waitForFunction(() => document.querySelectorAll('.bcv-fav').length === 6, null, { timeout: 5000 });
  await (await page.$$('.bcv-ccard .bcv-ccard__star'))[5].click();
  await page.waitForFunction(() => document.querySelectorAll('.bcv-ccard').length === 5, null, { timeout: 5000 });

  // ---- to do -------------------------------------------------------------------------------
  console.log('to do');
  await nav('todo');
  await page.waitForSelector('.bcv-head__sub', { timeout: 10000 });
  await waitText('.bcv-head__sub', /items across/);
  const todoSub = (await texts('.bcv-head__sub'))[0];
  check(/^\d+ items across \d+ courses$/.test(todoSub), `to do header: ${todoSub}`);
  const todoGroups = await texts('.bcv-group__head');
  check(todoGroups[0].startsWith('Today') && todoGroups[1].startsWith('Tomorrow') && todoGroups[2].startsWith('Next 7 days'), `to do groups: ${todoGroups.join(' | ')}`);
  check((await texts('.bcv-body .bcv-row')).some((t) => /Qz01.*F26-MATH 021 20 · Quiz · 10 pts.*11:59 PM/.test(t)), 'to do rows show course · kind · pts and time');
  await shot(page, '05-todo');
  const todoRows = (await page.$$('.bcv-body .bcv-card--list .bcv-row:not(.bcv-row--first)')).length;
  await page.click('.bcv-body .bcv-row .bcv-iconbtn');
  await page.waitForFunction((n) => document.querySelectorAll('.bcv-body .bcv-card--list .bcv-row:not(.bcv-row--first)').length === n - 1, todoRows, { timeout: 5000 });
  check(true, 'dismissing removes the item (planner override dismissed)');
  await page.click('.bcv-body .bcv-row .bcv-circle');
  await page.waitForFunction((n) => document.querySelectorAll('.bcv-body .bcv-card--list .bcv-row:not(.bcv-row--first)').length === n - 2, todoRows, { timeout: 5000 });
  check(true, 'ticking the circle marks an item complete');
  await page.click('.bcv-body .bcv-row--first .bcv-switch');
  await page.waitForSelector('.bcv-body .bcv-row--done', { timeout: 5000 });
  const doneRows = await texts('.bcv-body .bcv-row--done');
  check(doneRows.length >= 3 && doneRows.some((t) => /Dismissed.*Restore/.test(t)), `show completed reveals ${doneRows.length} done/dismissed rows with Restore`);
  await page.click('.bcv-body .bcv-row--done .bcv-circle');
  await page.waitForFunction((n) => document.querySelectorAll('.bcv-body .bcv-row--done').length === n - 1, doneRows.length, { timeout: 5000 });
  check(true, 'unticking a completed item reopens it');
  await page.click('.bcv-body .bcv-row--first .bcv-switch');
  await page.waitForFunction(() => !document.querySelector('.bcv-body .bcv-row--done'), null, { timeout: 5000 });
  await page.click('.bcv-seg__btn[data-value="course"]');
  check((await texts('.bcv-group__head')).some((t) => /F26-MATH 021 20/.test(t)), 'grouped by course');
  await page.click('.bcv-seg__btn[data-value="date"]');

  // ---- calendar --------------------------------------------------------------------------
  console.log('calendar');
  await nav('calendar');
  await page.waitForSelector('.bcv-cal__grid', { timeout: 10000 });
  check((await page.$$('.bcv-cal__day')).length === 42 && (await page.$('.bcv-cal__day--today')), 'month grid with today highlighted');
  await page.waitForSelector('.bcv-ev', { timeout: 10000 });
  const evs = await texts('.bcv-ev');
  check(evs.length >= 10 && evs.some((t) => /Dis01/.test(t)), `month view events: ${evs.length}`);
  check(await page.$('.bcv-ev__label.bcv-strike'), 'submitted/past events are struck through');
  const cals = await texts('.bcv-calrow__name');
  check(cals.length === 11 && cals[0] === 'Sam Student' && (await page.$$('.bcv-switch.is-on')).length === 10, `calendars list with switches: ${cals.length} (10 on, Canvas's limit)`);
  check((await texts('.bcv-calrow--refused .bcv-calrow__name'))[0] === 'Placement Exam: Chemistry' && /would not share one calendar \(Placement Exam: Chemistry\)/.test((await texts('.bcv-cal__notice'))[0]) && !evs.some((t) => /Chemistry placement/.test(t)), 'a calendar Canvas refuses (401) is retried alone, marked "Not shared", and the rest still load');
  await shot(page, '06-calendar-month');
  await (await page.$$('.bcv-switch'))[0].click();
  await page.waitForTimeout(400);
  check((await texts('.bcv-ev')).length < evs.length, 'switching a calendar off hides its events');
  await page.click('.bcv-seg__btn[data-value="week"]');
  await page.waitForSelector('.bcv-week__grid', { timeout: 5000 });
  check((await page.$$('.bcv-week__hour')).length === 16 && (await page.$('.bcv-week__col--today')), 'week view: 8a–11p rows, today column');
  check((await texts('.bcv-wev')).some((t) => /11:59p.*Dis01/.test(t)), 'week view events with times');
  await shot(page, '07-calendar-week');
  await page.click('.bcv-seg__btn[data-value="agenda"]');
  await page.waitForSelector('.bcv-agenda', { timeout: 5000 });
  check((await texts('.bcv-h1'))[0].includes(' – '), `agenda title: ${(await texts('.bcv-h1'))[0]}`);
  check((await page.$$('.bcv-mini__day')).length === 42 && (await page.$('.bcv-mini__day--start')) && (await page.$('.bcv-mini__day--end')), 'agenda range picker mini calendar');
  const agendaHeads = await texts('.bcv-agenda .bcv-group__head');
  check(agendaHeads[0].includes('Today ·') && /\d items?/.test(agendaHeads[0]), `agenda day heads: ${agendaHeads[0]}`);
  await shot(page, '08-calendar-agenda');
  // pick a range
  await (await page.$$('.bcv-mini__day:not(.bcv-mini__day--off)'))[2].click();
  await page.waitForTimeout(300);
  await (await page.$$('.bcv-mini__day:not(.bcv-mini__day--off)'))[5].click();
  await page.waitForTimeout(300);
  check((await texts('.bcv-mini__range-label'))[0].includes(' – '), `picked range: ${(await texts('.bcv-mini__range-label'))[0]}`);
  // the calendar API failing outright: planner items fill in, with a note
  const mockConfig = (cfg) => sw.evaluate(async (c) => (await fetch('http://localhost:8787/__mock/config', { method: 'POST', body: JSON.stringify(c) })).ok, cfg);
  await mockConfig({ calendarFail: true });
  await page.click('.bcv-seg__btn[data-value="month"]');
  await page.click('.bcv-cal__nav .bcv-iconbtn:nth-child(2)');
  await page.waitForSelector('.bcv-cal__notice--warn', { timeout: 10000 });
  check(/Canvas would not return calendar events \(calendar is having a moment\)\. Showing what the planner knows/.test((await texts('.bcv-cal__notice--warn'))[0]) && (await page.$$('.bcv-cal__day')).length === 42, 'when the calendar API fails, the planner fills the calendar in and says so');
  await mockConfig({ calendarFail: false });
  await page.click('.bcv-cal__nav .bcv-roundbtn');
  await page.waitForFunction(() => !document.querySelector('.bcv-cal__notice--warn'), null, { timeout: 10000 });

  // ---- inbox --------------------------------------------------------------------------------
  console.log('inbox');
  await nav('inbox');
  await page.waitForSelector('.bcv-inbox__list .bcv-row', { timeout: 10000 });
  const msgs = await texts('.bcv-inbox__list .bcv-row');
  check(msgs.length === 2 && /Halley Smith, Sam Student.*No submission for Acknowledge/.test(msgs[0]), `inbox rows: ${msgs[0].slice(0, 60)}`);
  check((await texts('.bcv-inbox__reader'))[0].includes('No conversation selected'), 'empty reader state');
  await shot(page, '09-inbox');
  await page.click('.bcv-inbox__list .bcv-row');
  await page.waitForSelector('.bcv-reader__msg', { timeout: 5000 });
  check((await texts('.bcv-reader__subject'))[0].startsWith('No submission') && (await texts('.bcv-reader__body'))[0].includes('Hello Bobcat'), 'reader shows the conversation');
  await page.fill('.bcv-reader__reply textarea', 'Thanks, done!');
  await page.click('.bcv-reader__reply .bcv-btn--primary');
  await page.waitForFunction(() => document.querySelectorAll('.bcv-reader__msg').length === 2, null, { timeout: 5000 });
  check(true, 'reply posted through the conversations API');
  await shot(page, '09b-inbox-reader');
  await page.click('.bcv-head .bcv-btn--primary');
  await page.waitForSelector('.bcv-compose', { timeout: 5000 });
  await page.fill('.bcv-recips input', 'yue');
  await page.waitForSelector('.bcv-compose .bcv-menu__item', { timeout: 5000 });
  await page.click('.bcv-compose .bcv-menu__item');
  check((await texts('.bcv-recip'))[0] === 'Yue Lei', 'recipient search adds a chip');
  await page.fill('.bcv-compose .bcv-input', 'Question about Dis01');
  await page.fill('.bcv-compose textarea', 'Could you clarify part b?');
  await shot(page, '09c-inbox-compose');
  await page.click('.bcv-compose .bcv-btn--primary');
  await page.waitForFunction(() => document.querySelectorAll('.bcv-inbox__list .bcv-row').length === 3, null, { timeout: 5000 });
  check(true, 'compose sends a new conversation');

  // ---- groups -----------------------------------------------------------------------------
  console.log('groups');
  await nav('groups');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  const gr = await texts('.bcv-body .bcv-row');
  check(gr.length === 2 && /Attestation Fall 2026 1.*Academic Success.*Collaboration team/.test(gr[0]) && /Study group B/.test(gr[1]), `groups: ${gr.join(' | ')}`);
  check((await texts('.bcv-body > div > .bcv-label')).join(',').toLowerCase() === 'current groups,previous groups', 'current / previous sections');
  await shot(page, '10-groups');
  await clickScreen('.bcv-body .bcv-row');
  await page.waitForSelector('.bcv-rail__item', { timeout: 10000 });
  check(page.url() === `${BASE}/groups/66729` && (await texts('.bcv-head h1'))[0] === 'Attestation Fall 2026 1', 'group opens in its own shell');
  check((await texts('.bcv-rail__item')).length === 8 && (await texts('.bcv-rail__title')).join(',').toLowerCase() === 'course,materials,people' && (await texts('.bcv-pill--term')).join(',').includes('4 members'), `group rail tabs: ${(await texts('.bcv-rail__item')).join(', ')}`);
  await page.waitForSelector('.bcv-act__title', { timeout: 10000 });
  check((await texts('.bcv-act__title'))[0] === 'Attestation due Friday' && (await texts('.bcv-body .bcv-label')).some((t) => /about/i.test(t)), 'group home: stream + About card');
  await shot(page, '10b-group-home');
  await tab('announcements');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  check((await texts('.bcv-body .bcv-row'))[0].includes('Attestation due Friday'), 'group announcements come from /api/v1/groups');
  await tab('people');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  check((await texts('.bcv-body .bcv-row')).length === 2 && (await texts('.bcv-badge')).includes('Member'), 'group people');

  // ---- course home ---------------------------------------------------------------------------
  console.log('course');
  await clickScreen('.bcv-fav');
  await page.waitForSelector('.bcv-rail__item', { timeout: 10000 });
  check(page.url() === `${BASE}/courses/101`, 'favourite opens the course page');
  const tabs = await texts('.bcv-rail__item');
  check(tabs.length === 10 && tabs[0] === 'Home' && tabs[6] === 'Modules' && (await texts('.bcv-rail__title')).join(',').toLowerCase() === 'course,materials,people,campus tools', `course rail groups the tabs from the API: ${tabs.join(', ')}`);
  check(await page.$eval('.bcv-screen--ctx .bcv-head__in', (el) => el.getBoundingClientRect().width === 1040), 'course screens are 1040px wide as in the mockup');
  check((await texts('.bcv-rail__ext')).join(',') === 'Resources & Policy', 'external tools are plain links under Campus tools');
  check(await page.$('.bcv-rail__item[data-tab="home"].is-active') && (await page.$eval('.bcv-rail__item.is-active .bcv-rail__tile', (el) => getComputedStyle(el).backgroundColor === 'rgb(52, 199, 89)')), 'active item takes the course colour');
  check(!(await page.$('.bcv-head .bcv-tab')), 'no tab pills under the course title');
  await waitText('.bcv-rail__item[data-tab="announcements"] .bcv-rail__count', /^2$/);
  const gradedCount = Number((await texts('.bcv-rail__item[data-tab="grades"] .bcv-rail__count'))[0]);
  check(gradedCount >= 3, `rail counts: 2 unread announcements, ${gradedCount} grades posted this week`);
  await page.click('.bcv-rail__toggle');
  check(await page.$('.bcv-rail.is-narrow') && (await page.$eval('.bcv-rail', (el) => el.getBoundingClientRect().width < 90)) && !(await visible('.bcv-rail__title')), 'rail collapses to tiles only');
  await shot(page, '11c-course-rail-narrow');
  await page.click('.bcv-rail__toggle');
  await page.waitForFunction(() => !document.querySelector('.bcv-rail.is-narrow'), null, { timeout: 3000 });
  check((await texts('.bcv-rail__toggle'))[0] === 'Collapse', 'toggle label follows the state');
  check((await texts('.bcv-head h1'))[0] === 'F26-MATH 021 20' && (await texts('.bcv-pill--term'))[0] === 'Fall 2026', 'course header');
  await page.waitForSelector('.bcv-front', { timeout: 10000 });
  check(/^front page · course information$/i.test((await texts('.bcv-front .bcv-label'))[0]), 'front page card');
  const chips = await texts('.bcv-front .bcv-chip');
  check(chips.length >= 3 && chips.includes('Course Syllabus'), `link chips from the front page: ${chips.join(', ')}`);
  check((await texts('.bcv-course-link')).join(',') === 'View Course Stream,View Course Calendar,View Course Notifications', 'course links');
  await page.waitForFunction(() => document.querySelectorAll('.bcv-row--p12-16 .bcv-row__title').length >= 3, null, { timeout: 10000 });
  check((await texts('.bcv-row--p12-16 .bcv-row__title')).some((t) => /Dis01/.test(t)), 'course To Do card');
  await shot(page, '11-course-home');
  await page.click('.bcv-head .bcv-btn--card');
  await page.waitForSelector('.bcv-reader-ov', { timeout: 5000 });
  check((await texts('.bcv-reader-ov h1'))[0] === 'Course Information', 'Immersive Reader overlay opens the front page');
  await shot(page, '11b-immersive-reader');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(100);
  if (await page.$('.bcv-reader-ov')) await page.click('.bcv-reader-ov .bcv-iconbtn');

  // announcements
  await tab('announcements');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  const anns = await texts('.bcv-body .bcv-row');
  check(anns.length === 4 && /Prerequisite Skills Test.*Yue Lei · All sections/.test(anns[0]), `announcements: ${anns[0].slice(0, 70)}`);
  await page.click('.bcv-seg__btn[data-value="unread"]');
  check((await page.$$('.bcv-body .bcv-row')).length === 2, 'unread filter');
  await shot(page, '12-course-announcements');
  await page.click('.bcv-body .bcv-row');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check((await texts('.bcv-detail__title'))[0] === 'Prerequisite Skills Test' && (await page.$$('.bcv-entry')).length === 2, 'announcement opens as a thread');

  // assignments
  await tab('assignments');
  await page.waitForSelector('.bcv-group__head', { timeout: 10000 });
  const agroups = await texts('.bcv-group__head');
  check(agroups[0].startsWith('Upcoming Assignments') && agroups.some((t) => /Past Assignments \d+ graded/.test(t)), `assignment groups: ${agroups.join(' | ')}`);
  const arows = await texts('.bcv-body .bcv-row');
  check(arows.some((t) => /Qz01.*Due .* at 11:59pm · –\/10 pts.*Not submitted/.test(t)) && arows.some((t) => /Lec05-PreQuiz.*19\/19 pts.*Graded/.test(t)), 'assignment rows: due, points, status badge');
  await shot(page, '13-course-assignments');
  await page.click('.bcv-seg__btn[data-value="type"]');
  await page.waitForFunction(() => [...document.querySelectorAll('.bcv-group__head')].some((e) => /Discussion Quizzes/.test(e.textContent)), null, { timeout: 5000 });
  check((await texts('.bcv-group__head')).some((t) => /Midterms 57% of grade/.test(t)), 'show by type uses assignment groups + weights');
  await page.click('.bcv-seg__btn[data-value="date"]');
  await page.waitForSelector('.bcv-group__head', { timeout: 5000 });
  const dis01 = (await page.$$('.bcv-body .bcv-row')).filter(async () => true);
  for (const r of dis01) if (/Dis01/.test(await r.textContent())) { await r.click(); break; }
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check((await texts('.bcv-detail__title'))[0] === 'Dis01' && (await texts('.bcv-detail__meta'))[0].includes('Points 10') && (await page.$$('.bcv-rubric__row')).length === 2, 'assignment detail with rubric');
  check((await texts('.bcv-btn--primary'))[0] === 'Submit in Canvas', 'submit button hands off to Canvas');
  await shot(page, '14-assignment');

  // discussions
  await tab('discussions');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  const drows = await texts('.bcv-body .bcv-row');
  check(drows.length === 4 && /Is there any discussion happening this week\?.*Last post.*23 unread.*23 replies/.test(drows[1]), `discussions: ${drows[1].slice(0, 80)}`);
  await shot(page, '15-course-discussions');
  await page.click('.bcv-body .bcv-row');
  await page.waitForSelector('.bcv-entry', { timeout: 10000 });
  check((await page.$$('.bcv-entry')).length === 2 && (await page.$('.bcv-entry--reply')), 'thread with nested replies');
  await page.fill('.bcv-reply textarea', 'My question is about removable discontinuities.');
  await page.click('.bcv-reply .bcv-btn--primary');
  await page.waitForFunction(() => document.querySelectorAll('.bcv-entry').length === 3, null, { timeout: 10000 });
  check(true, 'reply posted to the discussion entries API');
  await shot(page, '15b-discussion-thread');

  // grades
  await tab('grades');
  await page.waitForSelector('.bcv-rings__svg', { timeout: 10000 });
  check((await page.$$('.bcv-rings__svg > circle')).length === 8 && (await page.$$('.bcv-rings__svg pattern')).length === 2 && (await page.$$eval('.bcv-rings__svg > circle', (els) => els.filter((e) => /^url\(#bcv-stipple/.test(e.getAttribute('stroke'))).length)) === 2, 'four rings (total + 3 graded groups); the two 0%-weight rings are stippled');
  check((await texts('.bcv-gr__label'))[0].toLowerCase() === 'total' && (await texts('.bcv-gr__total'))[0] === '92%' && /^As shown in Canvas · A-\. Weighted across the groups that have graded work\.$/.test((await texts('.bcv-gr__note'))[0]), `total beside the rings: ${(await texts('.bcv-gr__total'))[0]} · ${(await texts('.bcv-gr__note'))[0]}`);
  const legend = await texts('.bcv-legend__row');
  check(legend[0] === 'Discussion Quizzes 10 / 10 pts · Skills_Check excluded 18% of grade 100%' && /^Effort \d+ \/ \d+ pts not weighted 65%$/.test(legend[1]) && /^Collaboration .* not weighted 100%$/.test(legend[2]), `by group, weighted groups first: ${legend.slice(0, 2).join(' | ')}`);
  check((await texts('.bcv-ungraded__name')).join(',') === 'Midterms,Final,Coursework (Knewton Alta)' && legend.some((t) => t === 'Midterms Nothing graded yet — no ring 57% —'), 'ungraded groups listed under the legend without a ring');
  const wbar = await texts('.bcv-wbar__row');
  check((await page.$$('.bcv-wbar__seg')).length === 3 && (await page.$$('.bcv-wbar__seg--ungraded')).length === 2 && wbar.join(' | ') === 'Discussion Quizzes 18% of grade 100% | Midterms 57% of grade ungraded | Final 25% of grade ungraded' && (await texts('.bcv-gr__hsub'))[0] === '100% of final grade', `weight bar: ${wbar.join(' | ')}`);
  check((await texts('.bcv-gr__wnote'))[0] === 'Effort, Collaboration and Coursework (Knewton Alta) carry 0% weight — they show as rings but never move the total.', `0%-weight note: ${(await texts('.bcv-gr__wnote'))[0]}`);
  const gradeRows = await texts('.bcv-grades__main .bcv-row');
  check(gradeRows.length === 17 && /Lec01-PreQuiz.*Effort · due .* by 10:30am · submitted .*13 \/ 16/.test(gradeRows[0]), `grade rows: ${gradeRows[0]}`);
  check(gradeRows.some((t) => /Skills_Check.*Not counted toward final grade/.test(t)) && gradeRows.some((t) => /Transformation.*Late/.test(t)), 'late / not-counted badges');
  check(!(await page.$('.bcv-grades__side')) && !(await texts('.bcv-label')).some((t) => /assignment group weights/i.test(t)), 'no separate group-weights card: the weights live in the grade card');
  await shot(page, '16-course-grades');
  await page.click('.bcv-whatif-btn');
  await page.waitForSelector('.bcv-banner', { timeout: 5000 });
  check((await texts('.bcv-banner__title'))[0] === 'This is not your actual score.', 'what-if banner');
  const inputs = await page.$$('.bcv-whatif__input');
  check(inputs.length === 17, 'what-if inputs for every row');
  const midterm = (await page.$$('.bcv-grades__main .bcv-row')).find(async () => true);
  void midterm;
  const rows = await page.$$('.bcv-grades__main .bcv-row');
  for (const r of rows) if (/Midterm 1/.test(await r.textContent())) { const inp = await r.$('.bcv-whatif__input'); await inp.fill('80'); await inp.press('Enter'); break; }
  await page.waitForFunction(() => document.querySelector('.bcv-whatif__input.is-hyp'), null, { timeout: 5000 });
  const legend2 = await texts('.bcv-legend__row');
  check((await texts('.bcv-gr__label'))[0].toLowerCase() === 'what-if total' && /^What-if weighted/.test((await texts('.bcv-gr__note'))[0]) && legend2.some((t) => t === 'Midterms 80 / 100 pts · includes what-if 57% of grade 80%') && (await texts('.bcv-wbar__row')).some((t) => t === 'Midterms 57% of grade 80%') && (await page.$('.bcv-whatif-btn.is-on')), `what-if recomputes: ${(await texts('.bcv-gr__total'))[0]} | ${legend2.find((t) => /Midterms/.test(t))}`);
  check((await page.$$('.bcv-rings__svg > circle')).length === 10 && (await page.$$eval('.bcv-rings__svg > circle', (els) => els.every((e) => !/#(0a84ff|34c759|ff9500|30b0c7)/i.test(e.getAttribute('stroke') || '')))), 'what-if adds the Midterms ring and greys every ring');
  await shot(page, '17-grades-whatif');
  await page.click('.bcv-banner .bcv-btn');
  await page.click('.bcv-whatif-btn');
  await page.waitForFunction(() => !document.querySelector('.bcv-banner'), null, { timeout: 5000 });

  // people
  await tab('people');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  const prow = await texts('.bcv-body .bcv-row');
  check(prow.length === 7 && /Alan Aguilar He\/Him\/His Discussion-24D · Lecture-20 Student/.test(prow[1]) && /Yue Lei.*Teacher/.test(prow[6]), `people: ${prow[1]}`);
  await page.click('.bcv-pill:not(.bcv-pill--term)');
  await page.click('.bcv-menu__item:nth-child(3)');
  await page.waitForFunction(() => document.querySelectorAll('.bcv-body .bcv-row').length === 1, null, { timeout: 5000 });
  check(true, 'role filter');
  await shot(page, '18-course-people');

  // pages
  await tab('pages');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  const pg = await texts('.bcv-body .bcv-row');
  check(pg.length === 2 && /Course Information Front page Created .* · last edited .* by Yue Lei/.test(pg[0]), `pages: ${pg[0]}`);
  check((await texts('.bcv-hint'))[0].startsWith('This course has 2 published pages.'), 'pages hint');
  await shot(page, '19-course-pages');
  await (await page.$$('.bcv-body .bcv-row'))[1].click();
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check((await texts('.bcv-detail__title'))[0] === 'Chapter 4 notes' && (await page.$('.bcv-prose h2')), 'page view renders the body');

  // files
  await tab('files');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  const frows = await texts('.bcv-body .bcv-row');
  check(frows.length === 5 && /Course Information Folder · modified/.test(frows[0]) && /Course Syllabus.pdf PDF · modified .* 212 KB/.test(frows[4]), `files: ${frows[4]}`);
  check(await page.$eval('.bcv-btn--fill36', (b) => b.disabled), 'Download disabled until a file is selected');
  await (await page.$$('.bcv-body .bcv-row'))[4].click();
  check(await page.$eval('.bcv-btn--fill36', (b) => !b.disabled) && (await page.$('.bcv-row.is-selected')), 'selecting a file enables Download');
  await shot(page, '20-course-files');
  await page.click('.bcv-body .bcv-row');
  await page.waitForSelector('.bcv-crumbs', { timeout: 10000 });
  check(page.url().endsWith('/files/folder/Course%20Information') && (await texts('.bcv-body .bcv-row'))[0].includes('Resources_Policy.pdf'), 'folder navigation by path');

  // quizzes
  await tab('quizzes');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  const qlabels = await texts('.bcv-body .bcv-label');
  check(qlabels.join(',').toLowerCase() === 'assignment quizzes,practice quizzes', `quiz groups: ${qlabels.join(', ')}`);
  check((await texts('.bcv-body .bcv-row')).some((t) => /Lec06-PreQuiz Due .* at 10:30am · 17 pts · 4 questions/.test(t)), 'quiz rows');
  await shot(page, '21-course-quizzes');
  await page.click('.bcv-body .bcv-row');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check((await texts('.bcv-detail__meta'))[0].includes('Time limit 20 minutes') && (await texts('.bcv-grades__side, .bcv-col .bcv-row')).some((t) => /Attempt 1/.test(t)), 'quiz detail with attempts');

  // modules
  await tab('modules');
  await page.waitForSelector('.bcv-module', { timeout: 10000 });
  check((await page.$$('.bcv-module')).length === 1 && (await texts('.bcv-module__item')).length === 2, 'modules list');
  await page.goto(`${BASE}/courses/102/modules`);
  await page.waitForSelector('.bcv-module', { timeout: 10000 });
  const mods = await texts('.bcv-module__head');
  check(mods.length === 3 && /Week 1: Kinematics 2 of 2 requirements done/.test(mods[0]) && /Week 3: Energy Locked until/.test(mods[2]), `modules: ${mods.join(' | ')}`);
  check((await page.$$('.bcv-circle.is-done')).length === 3 && (await page.$('.bcv-indent-1')), 'completion marks and indents');
  await shot(page, '22-course-modules');

  // course home with modules as the default view + syllabus home
  await page.goto(`${BASE}/courses/102`);
  await page.waitForSelector('.bcv-module', { timeout: 10000 });
  check(true, 'home shows modules when the course uses that view');
  await page.goto(`${BASE}/courses/104`);
  await page.waitForSelector('.bcv-front', { timeout: 10000 });
  check(/^syllabus$/i.test((await texts('.bcv-front .bcv-label'))[0]), 'home shows the syllabus when the course uses that view');
  await page.goto(`${BASE}/courses/101/assignments/syllabus`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check((await texts('.bcv-grades__side .bcv-row, .bcv-col .bcv-row')).length >= 10, 'syllabus page lists dated assignments');

  // ---- quiz in progress: focus mode ---------------------------------------------------------------
  console.log('quiz focus');
  await page.goto(`${BASE}/courses/101/quizzes/9011/take`);
  await page.waitForSelector('html.bcv-punch #submit_quiz_form', { timeout: 10000 });
  check(await page.$('#bcv-app.bcv-focus') && !(await visible('.bcv-nav')) && (await texts('.bcv-focus__title'))[0] === 'Quiz in progress', 'quiz page hides navigation');
  check((await visible('#right-side-wrapper')) && (await texts('#right-side'))[0].includes("Canvas's own sidebar"), "Canvas's own right column (question list, timer) stays beside the quiz");
  page.once('dialog', (d) => d.dismiss());
  await page.click('.bcv-focus__card .bcv-btn');
  await page.waitForTimeout(300);
  check(page.url().endsWith('/quizzes/9011/take'), 'leaving a quiz asks first; cancelling stays');
  await shot(page, '22b-quiz-focus');

  // ---- quiz flow (mockup): intro → questions → review → submitted -----------------------------
  console.log('quiz flow');
  await page.goto(`${BASE}/courses/101/quizzes/9011`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  await page.click('.bcv-detail__actions .bcv-btn--primary');
  await page.waitForSelector('.bcv-qz__begin', { timeout: 10000 });
  check(page.url().endsWith('/quizzes/9011?bcv=take') && !(await visible('.bcv-side')) && !(await visible('#bcv-fab')), 'quiz flow opens with the sidebar and smart button hidden');
  check((await texts('.bcv-qz__h1'))[0] === 'Lec06-PreQuiz' && (await texts('.bcv-qz__bullet')).some((t) => /Time limit: 20 minutes/.test(t)) && (await texts('.bcv-qz__begin'))[0] === 'Begin attempt', 'intro card lists the quiz settings');
  check((await texts('.bcv-qz__clock'))[0] === '20 min', 'timer pill shows the limit before the attempt starts');
  await shot(page, '22c-quiz-intro');
  await page.click('.bcv-qz__begin');
  await page.waitForSelector('.bcv-qz__opt', { timeout: 10000 });
  check((await page.$$('.bcv-qz__pill')).length === 4 && (await page.$('.bcv-qz__pill:first-child.is-current')) && (await texts('.bcv-qz__qnum'))[0] === 'Question 1', 'attempt started through the API: progress pills and question 1');
  check(/^(19|20):\d\d$/.test((await texts('.bcv-qz__clock'))[0]), `timer counts down from the attempt's end_at: ${(await texts('.bcv-qz__clock'))[0]}`);
  check((await page.$$('.bcv-qz__letter')).length === 5 && (await texts('.bcv-qz__letter')).join('') === 'ABCDE', 'lettered options');
  await page.click('.bcv-qz__opt');
  await page.waitForSelector('.bcv-qz__opt.is-selected', { timeout: 5000 });
  await waitText('.bcv-qz__answered', /1 of 4 answered · Saved/);
  check((await page.$$('.bcv-qz__pill.is-answered')).length === 1, 'picking an option saves the answer (quiz submission questions API)');
  await page.click('.bcv-qz__flag');
  await page.waitForSelector('.bcv-qz__flag.is-on', { timeout: 5000 });
  check(await page.$('.bcv-qz__pill:first-child.is-flagged'), 'flag for review marks the pill');
  await shot(page, '22d-quiz-question');
  await page.click('.bcv-qz__btn--next');
  await waitText('.bcv-qz__qnum', /Question 2/);
  check(await page.$('.bcv-qz__pill:nth-child(2).is-current'), 'Next moves to question 2');
  await page.click('.bcv-qz__pill:first-child');
  await waitText('.bcv-qz__qnum', /Question 1/);
  check(await page.$('.bcv-qz__opt.is-selected'), 'pills jump between questions and answers are kept');
  // scroll-all mode
  await page.click('.bcv-qz__mode:nth-child(2)');
  await page.waitForSelector('.bcv-qz__page--all', { timeout: 5000 });
  check((await page.$$('.bcv-qz__q')).length === 4 && (await page.$$('.bcv-qz__progress--all .bcv-qz__pill')).length === 4, 'scroll mode shows every question on one page');
  await page.click('#bcv-q1 .bcv-qz__opt:nth-child(2)');
  await waitText('.bcv-qz__answered', /2 of 4 answered/);
  await page.fill('#bcv-q3 input', '3.15');
  await waitText('.bcv-qz__answered', /3 of 4 answered · Saved/);
  await shot(page, '22e-quiz-scroll');
  // an in-page link would leave the attempt: the browser asks, cancelling stays
  page.once('dialog', (d) => d.dismiss());
  await page.click('#bcv-q3 .bcv-qz__qtext a');
  await page.waitForTimeout(400);
  check(page.url().endsWith('/quizzes/9011?bcv=take') && (await page.$('.bcv-qz__page--all')), 'leaving mid-attempt asks first; cancelling stays');
  await page.click('.bcv-qz__foot .bcv-qz__btn--primary');
  await page.waitForSelector('.bcv-qz__sum', { timeout: 5000 });
  const sums = await texts('.bcv-qz__sum');
  check(sums.length === 4 && /^Q1.*-3\.15 m\/s$/.test(sums[0]) && /Q3.*Not answered/.test(sums[2]) && /^Q4.*3\.15$/.test(sums[3]) && /3 of 4 answered · 1 left blank/.test((await texts('.bcv-qz__lead'))[0]), `review lists every answer: ${sums.join(' | ')}`);
  await shot(page, '22f-quiz-review');
  await page.click('.bcv-qz__sum:nth-child(3)');
  await page.waitForSelector('.bcv-qz__page--all', { timeout: 5000 });
  check(await page.$('.bcv-qz__progress--all .bcv-qz__pill:nth-child(3).is-current'), 'tapping a review row goes back to that question');
  await page.click('#bcv-q2 .bcv-qz__opt:nth-child(1)');
  await page.click('#bcv-q2 .bcv-qz__opt:nth-child(3)');
  await waitText('.bcv-qz__answered', /4 of 4 answered · Saved/);
  check((await page.$$('#bcv-q2 .bcv-qz__opt.is-selected')).length === 2, 'multiple-answer questions keep every pick');
  await page.click('.bcv-qz__foot .bcv-qz__btn--primary');
  await page.waitForSelector('.bcv-qz__big--primary', { timeout: 5000 });
  page.once('dialog', (d) => d.accept());
  await page.click('.bcv-qz__big--primary');
  await page.waitForSelector('.bcv-qz__done', { timeout: 10000 });
  const doneCards = await texts('.bcv-qz__donecard');
  // Q2 was answered wrong on purpose (-2 m): 4 + 0 + 4 + 5 of 17
  check((await texts('.bcv-qz__h1'))[0] === 'Attempt submitted' && /Questions answered 4 of 4 answered/.test(doneCards[0]) && /Score 13 \/ 17/.test(doneCards[1] || ''), `submitted screen shows the score Canvas returned: ${doneCards.join(' | ')}`);
  await shot(page, '22g-quiz-done');
  await page.click('.bcv-qz__donebtns .bcv-qz__big--primary');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(page.url() === `${BASE}/courses/101/quizzes/9011` && (await texts('.bcv-grades__side, .bcv-col .bcv-row')).some((t) => /Attempt 1/.test(t)), 'back to the quiz page, which now lists the attempt');
  // one question at a time + no going back
  await page.goto(`${BASE}/courses/101/quizzes/9014?bcv=take`);
  await page.waitForSelector('.bcv-qz__begin', { timeout: 10000 });
  check((await texts('.bcv-qz__bullet')).some((t) => /cannot go back/.test(t)), 'one-at-a-time / no-going-back quizzes say so up front');
  await page.click('.bcv-qz__begin');
  await page.waitForSelector('.bcv-qz__opt', { timeout: 10000 });
  check(!(await visible('.bcv-qz__modes')) && (await texts('.bcv-qz__foot .bcv-qz__btn')).join(',') === 'Next', 'no mode switch and no Back button when the quiz forbids it');
  await page.click('.bcv-qz__btn--next');
  await waitText('.bcv-qz__qnum', /Question 2/);
  check(await page.$('.bcv-qz__pill:first-child:disabled'), 'earlier questions lock when the quiz says no going back');
  await page.click('.bcv-qz__exit');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(page.url() === `${BASE}/courses/101/quizzes/9014` && (await texts('.bcv-detail__actions .bcv-btn--primary'))[0] === 'Resume attempt', 'Save and exit keeps the attempt open: the quiz page offers to resume it');

  // ---- hybrid (native) page inside the shell ----------------------------------------------------
  console.log('native pages');
  await page.goto(`${BASE}/courses/101/external_tools/9`);
  await page.waitForSelector('html.bcv-punch #content', { timeout: 10000 });
  await page.waitForFunction(() => document.getElementById('content').getBoundingClientRect().left > 400, null, { timeout: 5000 });
  check((await page.$$eval('.bcv-rail__ext.is-active', (els) => els.map((e) => e.textContent.trim())))[0] === 'Resources & Policy', 'external tool link active in the rail');
  const punch = await page.evaluate(() => {
    const c = document.getElementById('content').getBoundingClientRect();
    const rail = document.querySelector('.bcv-rail').getBoundingClientRect();
    const head = document.querySelector('.bcv-screen--ctx .bcv-head').getBoundingClientRect();
    return { parent: document.getElementById('content').parentElement.id, left: c.left, railRight: rail.right, top: c.top, headBottom: head.bottom, width: c.width, tool: !!document.querySelector('#content #tool_content'), app: getComputedStyle(document.getElementById('bcv-app')).position };
  });
  check(punch.parent === 'not_right_side' && punch.tool && punch.app === 'fixed', `Canvas's page stays in its own DOM (tool iframe never re-parented): ${JSON.stringify(punch)}`);
  check(punch.left >= punch.railRight && punch.top >= punch.headBottom - 1 && punch.width > 400, `Canvas content is laid out into the hole beside the rail and under the header: ${JSON.stringify(punch)}`);
  let clickable = true;
  try { await page.click('#content h2', { timeout: 3000 }); } catch { clickable = false; }
  check(clickable, 'clicks reach Canvas\'s content through the overlay');
  await page.click('.bcv-rail__toggle');
  await page.waitForFunction(() => document.getElementById('content').getBoundingClientRect().left < 400, null, { timeout: 5000 });
  check(true, 'collapsing the rail re-measures the hole and Canvas content moves over');
  await page.click('.bcv-rail__toggle');
  await page.waitForFunction(() => document.getElementById('content').getBoundingClientRect().left > 400, null, { timeout: 5000 });
  await shot(page, '23-native-tool');
  await page.goto(`${BASE}/profile`);
  await page.waitForSelector('html.bcv-punch #content', { timeout: 10000 });
  check((await texts('.bcv-head h1'))[0] === 'Sam Student' && (await visible('#content')) && !(await visible('#header')), 'unknown page: Canvas content inside the global shell, Canvas chrome hidden');

  await page.goto(`${BASE}/courses/104/assignments/4003`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(await page.$eval('.bcv-frame', (f) => /external_tools\/retrieve\?assignment_id=4003/.test(f.getAttribute('src'))), 'external-tool assignment embeds the tool launch');
  check(!(await texts('.bcv-btn--primary')).includes('Submit in Canvas'), 'no submit button for tool assignments');
  await shot(page, '14b-assignment-tool');

  // ---- smart panel --------------------------------------------------------------------------------
  console.log('smart panel');
  await page.goto(`${BASE}/courses/101/assignments/1009`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  await page.click('#bcv-fab');
  await page.waitForSelector('#bcv-smart', { timeout: 5000 });
  check((await texts('.bcv-smart__ctx'))[0] === 'Reading: F26-MATH 021 20 · Dis01', `smart context: ${(await texts('.bcv-smart__ctx'))[0]}`);
  const actions = await texts('.bcv-smart__action-label');
  check(actions.join(',') === 'Summarize this assignment,Make a checklist', `suggested actions: ${actions.join(', ')}`);
  check(!(await page.content()).match(/\bAI\b/), 'the interface never says "AI"');
  await shot(page, '24-smart-panel');
  await page.fill('#bcv-smart textarea', 'What is this about?');
  await page.press('#bcv-smart textarea', 'Enter');
  await page.waitForSelector('.bcv-bubble--error', { timeout: 10000 });
  check(/Add a Claude or ChatGPT key/.test((await texts('.bcv-bubble--error'))[0]) && (await page.$('.bcv-bubble--error .bcv-btn')), 'no key → setup message with a Settings button');
  await setSettings({ smart: { claudeKey: 'sk-ant-test-not-real' } });
  await page.click('.bcv-smart__action');
  await page.waitForFunction(() => document.querySelectorAll('.bcv-bubble--error').length === 2, null, { timeout: 30000 });
  check(/Claude/.test((await texts('.bcv-bubble--error'))[1]), `provider error surfaced: ${(await texts('.bcv-bubble--error'))[1].slice(0, 60)}`);
  await setSettings({ smart: { claudeKey: '' } });
  await page.click('#bcv-smart .bcv-iconbtn');
  await page.waitForFunction(() => !document.querySelector('#bcv-smart'), null, { timeout: 5000 });

  // ---- dark appearance -------------------------------------------------------------------------------
  console.log('appearance');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-theme-btn', { timeout: 10000 });
  await page.click('#bcv-theme-btn');
  await page.waitForFunction(() => document.documentElement.getAttribute('data-bcv-theme') === 'dark', null, { timeout: 5000 });
  await page.waitForSelector('.bcv-stat', { timeout: 10000 });
  check((await texts('#bcv-theme-btn'))[0] === 'Light appearance', 'dark appearance toggled from the sidebar');
  check(await page.$eval('#bcv-app', (el) => getComputedStyle(el).backgroundColor === 'rgb(0, 0, 0)'), 'dark background');
  await shot(page, '25-dark-dashboard');
  await page.goto(`${BASE}/courses/101/grades`);
  await page.waitForSelector('.bcv-rings__svg', { timeout: 10000 });
  await shot(page, '26-dark-grades');
  await page.goto(`${BASE}/courses/101`);
  await page.waitForSelector('.bcv-front', { timeout: 10000 });
  await shot(page, '27-dark-course-home');
  await page.click('#bcv-theme-btn');
  await page.waitForFunction(() => document.documentElement.getAttribute('data-bcv-theme') === 'light', null, { timeout: 5000 });

  // ---- skin switch ----------------------------------------------------------------------------------
  console.log('skin switch');
  await page.goto(`${BASE}/courses/101/external_tools/9`);
  await page.waitForSelector('html.bcv-punch #content', { timeout: 10000 });
  await page.click('#bcv-skin');
  await page.waitForFunction(() => !document.documentElement.classList.contains('bcv-on'), null, { timeout: 5000 });
  check(await visible('#application') && !(await visible('#bcv-app')), 'skin off: stock Canvas is back');
  check(!(await page.$('html.bcv-punch')) && (await visible('#header')) && (await page.$eval('#content', (el) => el.getBoundingClientRect().left < 200)), 'skin off ends the punch-through: Canvas lays its page out itself again');
  check((await texts('#bcv-skin'))[0] === 'Skin off' && !(await page.$('#bcv-fab')), 'switch shows off state, smart button hidden');
  await shot(page, '28-skin-off');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-skin', { timeout: 10000 });
  check(!(await page.$('#bcv-app')) || !(await visible('#bcv-app')), 'skin stays off on the next page load');
  await page.click('#bcv-skin');
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  check(await visible('#bcv-app'), 'skin switched back on in place');
  await nav('courses');
  await page.waitForSelector('.bcv-ccard__hero--term', { timeout: 10000 });
  await page.click('#bcv-skin');
  await page.waitForFunction(() => !document.documentElement.classList.contains('bcv-on'), null, { timeout: 5000 });
  check(page.url() === `${BASE}/courses` && (await visible('#application')) && (await page.title()).includes('courses'), 'skin off after navigating shows the same page in stock Canvas (it was underneath all along)');
  await page.click('#bcv-skin');
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });

  // ---- extension pages ---------------------------------------------------------------------------------
  console.log('extension pages');
  const options = await context.newPage();
  await options.goto(`chrome-extension://${extId}/options/options.html`);
  await options.waitForSelector('#claudeKey', { timeout: 5000 });
  await options.fill('#openaiKey', 'sk-test');
  await options.dispatchEvent('#openaiKey', 'change');
  await options.waitForTimeout(300);
  check(/ChatGPT/.test(await options.$eval('#smartStatus', (el) => el.textContent)), 'options status reflects the key');
  await options.click('#testOpenAI');
  await options.waitForFunction(() => /rejected|Could not|works|Unauthorized|invalid/i.test(document.querySelector('#openaiResult').textContent), null, { timeout: 20000 });
  console.log('   key test result:', await options.$eval('#openaiResult', (el) => el.textContent));
  await options.fill('#openaiKey', '');
  await options.dispatchEvent('#openaiKey', 'change');
  await options.screenshot({ path: join(out, '29-options.png'), fullPage: true });
  const popupPage = await context.newPage();
  await popupPage.goto(`chrome-extension://${extId}/popup/popup.html`);
  await popupPage.waitForTimeout(500);
  await popupPage.screenshot({ path: join(out, '30-popup.png') });
} catch (e) {
  console.error('smoke test crashed:', e);
  failures.push('crash: ' + e.message);
} finally {
  await context.close();
  server.kill();
  rmSync(extDir, { recursive: true, force: true });
  rmSync(userDataDir, { recursive: true, force: true });
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);

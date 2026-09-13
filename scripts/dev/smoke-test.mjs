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

// 3. the native projects ship every top-level entry of extension/ and carry the same version
{
  const { readdirSync } = await import('node:fs');
  const pbx = readFileSync(join(root, 'macos', 'Simpl Courses', 'Simpl Courses.xcodeproj', 'project.pbxproj'), 'utf8');
  const iosYml = readFileSync(join(root, 'ios', 'project.yml'), 'utf8');
  console.log('\nbundles');
  for (const entry of readdirSync(join(root, 'extension')).filter((n) => !n.startsWith('.'))) {
    check(pbx.includes(`path = ../../../extension/${entry};`), `Mac app bundles extension/${entry}`);
  }
  check(pbx.split(`MARKETING_VERSION = ${manifest.version};`).length === 5, `Mac app version is ${manifest.version}`);
  check(iosYml.includes(`MARKETING_VERSION: "${manifest.version}"`), `iOS app version is ${manifest.version}`);
}

const userDataDir = join(tmpdir(), `bcv-profile-${Date.now()}`);
const context = await chromium.launchPersistentContext(userDataDir, {
  channel: 'chromium',
  headless: true,
  viewport: { width: 1400, height: 900 },
  args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`],
});
let page; // hoisted so a crash can say what the page was doing
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  const extId = new URL(sw.url()).host;
  console.log('extension id', extId);
  const setSettings = (patch) => sw.evaluate(async (p) => self.BCV.settings.update(p), patch);
  const isSetup = (p) => p.url().endsWith('/setup/setup.html');
  if (!context.pages().some(isSetup)) await context.waitForEvent('page', { timeout: 8000 }).catch(() => null);
  await new Promise((r) => setTimeout(r, 800)); // the install offers the page from two places at once (onInstalled, the background starting): both have run by now
  const setupTabs = context.pages().filter(isSetup);
  check(setupTabs.length === 1, `installing the extension opens the guided setup page — once, not twice (${setupTabs.length} open)`);
  for (const t of setupTabs) await t.close();
  check((await sw.evaluate(async () => (await self.BCV.api.storage.local.get('setup:flow'))['setup:flow'])) === 2, 'the build records its setup flow, so an update from an older flow offers the page once more');
  // Safari turns the extension on without an install event, so the page is offered again on a new
  // browser session while setup is unfinished — and never again once it is done or skipped.
  const setupPages = () => context.pages().filter((pg) => pg.url().endsWith('/setup/setup.html'));
  const offerAgain = async () => {
    for (const pg of setupPages()) await pg.close();
    await sw.evaluate(async () => { await self.BCV.api.storage.session?.remove('setup:shown'); }); // a new browser session
    await sw.evaluate(async () => { await self.BCV.background.offerSetup(); });
    await new Promise((r) => setTimeout(r, 900));
    const opened = setupPages();
    for (const pg of opened) await pg.close();
    return opened.length > 0;
  };
  check(await offerAgain(), 'the setup page opens again on a new browser session while setup is unfinished (Safari enables without installing)');
  await sw.evaluate(() => self.BCV.api.storage.local.set({ 'setup:done': true }));
  check(!(await offerAgain()), 'once setup is done or skipped it never opens again');
  await sw.evaluate(() => self.BCV.api.storage.local.remove('setup:done'));
  await sw.evaluate(() => self.BCV.api.storage.local.set({ 'setup:offered': true }));

  page = await context.newPage();
  // a page is ready to poke once it is drawn and nothing painted from the cache is still waiting on Canvas
  const __goto = page.goto.bind(page);
  page.gotoRaw = __goto;
  // …and no counter is still rolling to its value (mockup 11 entry motion, under a second)
  const rolled = () => page.waitForFunction(() => !document.querySelector('[data-rolling]'), null, { timeout: 5000 }).catch(() => {});
  page.goto = async (...a) => { const r = await __goto(...a); await page.waitForFunction(() => { const c = document.documentElement.classList; return !c.contains('bcv-on') || c.contains('bcv-settled'); }, null, { timeout: 20000 }).catch(() => {}); await rolled(); return r; };
  page.on('pageerror', (e) => console.log('  page error:', e.message));
  page.on('console', (m) => { if (m.type() === 'error') console.log('  console:', m.text()); });
  const texts = (sel) => page.$$eval(sel, (els) => els.map((e) => (e.innerText || e.textContent).replace(/\s+/g, ' ').trim()));
  const visible = (sel) => page.$eval(sel, (el) => getComputedStyle(el).display !== 'none').catch(() => false);
  const waitText = (sel, re) => page.waitForFunction(([s, r]) => [...document.querySelectorAll(s)].some((e) => new RegExp(r).test(e.textContent)), [sel, re.source], { timeout: 10000 });
  // click something that re-renders the main screen, and wait until the old screen element is gone
  // (inside a course only the main column changes hands: the header and rail stay put)
  const clickScreen = async (sel) => {
    await page.evaluate(() => { for (const m of document.querySelectorAll('#bcv-main > *, #bcv-main .bcv-cmain > *')) m.dataset.old = '1'; });
    await page.click(sel);
    await page.waitForSelector('#bcv-main > *:not([data-old]), #bcv-main .bcv-cmain > *:not([data-old])', { timeout: 10000 });
    await page.waitForFunction(() => { const c = document.documentElement.classList; return !c.contains('bcv-on') || c.contains('bcv-settled'); }, null, { timeout: 20000 }).catch(() => {});
    await rolled();
  };
  const tab = (id) => clickScreen(`.bcv-rail [data-tab="${id}"]`);
  const eventually = async (fn, ms = 6000) => {
    const t = Date.now();
    while (Date.now() - t < ms) {
      if (await fn()) return true;
      await new Promise((r) => setTimeout(r, 150));
    }
    return false;
  };
  const nav = (id) => clickScreen(`.bcv-nav__item[data-nav="${id}"]`);

  // ---- dashboard --------------------------------------------------------------------
  console.log('dashboard');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  check(await visible('#bcv-app'), 'app shell visible');
  check(!(await visible('#application')), 'stock Canvas hidden');
  check(!(await page.$('#bcv-skin')), 'no switch floats on the page (the look is toggled from the popup and settings)');
  await page.waitForSelector('.bcv-nav__item', { timeout: 10000 });
  const brand = await page.evaluate(() => {
    const img = document.querySelector('.bcv-brand__logo img');
    const cs = img ? getComputedStyle(img) : {};
    return { src: img?.getAttribute('src')?.slice(0, 18), radius: cs.borderRadius, fit: cs.objectFit, height: img?.getBoundingClientRect().height, text: document.querySelector('.bcv-brand')?.textContent.trim(), name: !!document.querySelector('.bcv-brand__name, .bcv-brand__sub') };
  });
  const tile = await page.$eval('.bcv-brand__logo', (el) => { const r = el.getBoundingClientRect(); return { w: Math.round(r.width), h: Math.round(r.height), radius: getComputedStyle(el).borderRadius }; });
  check(brand.src === 'data:image/svg+xml' && brand.radius === '0px' && brand.fit === 'contain' && brand.height <= 30 && tile.w === 40 && tile.h === 40 && tile.radius === '11px' && brand.name && brand.text.length > 0, `brand row: the school's mark whole inside a 40px rounded tile, the site name beside it: ${JSON.stringify({ ...brand, tile })}`);
  await page.waitForFunction(() => document.querySelectorAll('.bcv-fav__dot').length >= 5, null, { timeout: 10000 }).catch(() => {});
  const favDots = await page.$$eval('.bcv-fav__dot', (els) => els.map((e) => getComputedStyle(e).backgroundColor));
  check(favDots.length === 5 && new Set(favDots).size === 5 && favDots[0] === 'rgb(52, 199, 89)', `favourite dots carry the user's own course colours from Canvas: ${favDots.join(' | ')}`);
  // what the school added to Canvas's own nav (read from the page's #menu): tools, History, Help
  check((await texts('.bcv-nav__item--more')).join(',') === 'History,My Materials,Help' && (await page.$('.bcv-nav__item--more[data-extra="tool"] .bcv-nav__ic img')) !== null, `"More from Canvas" carries the school's own nav entries with the tool's icon: ${(await texts('.bcv-nav__item--more')).join(', ')}`);
  // mockup 11: nav icons are bare glyphs in their own colour, full strength on the active row and dimmed elsewhere
  const glyphs = await page.evaluate(() => {
    const ic = (k) => document.querySelector(`.bcv-nav__item[data-nav="${k}"] .bcv-nav__ic`);
    const svg = (k) => getComputedStyle(ic(k).querySelector('svg'));
    return { tile: getComputedStyle(ic('dashboard')).backgroundColor, dash: svg('dashboard').stroke, dashOpacity: svg('dashboard').opacity, todo: svg('todo').stroke, todoOpacity: svg('todo').opacity, size: ic('todo').querySelector('svg').getAttribute('width') };
  });
  check(glyphs.tile === 'rgba(0, 0, 0, 0)' && glyphs.dash === 'rgb(10, 108, 255)' && glyphs.dashOpacity === '1' && glyphs.todo === 'rgb(52, 199, 89)' && glyphs.todoOpacity === '0.62' && glyphs.size === '21', `nav glyphs: no tile, own colour, active at full strength: ${JSON.stringify(glyphs)}`);
  await page.click('.bcv-nav__item--more[data-extra="history"]');
  await page.waitForSelector('.bcv-sheet-ov .bcv-xrow', { timeout: 10000 });
  check((await texts('.bcv-xrow__t')).join(',') === 'Composition of Functions,Lec06-PreQuiz,Week 2 Post Class Assignment: GC articles' && /F26-MATH 021 20 · Assignment · /.test((await texts('.bcv-xrow__s'))[0]), `History opens Canvas's recently-visited list as a sheet: ${(await texts('.bcv-xrow__s'))[0]}`);
  await page.click('.bcv-xrow');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(page.url() === `${BASE}/courses/101/assignments/1002`, 'a history row opens the item (an absolute visited_url becomes a path)');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-nav__item--more[data-extra="help"]', { timeout: 10000 });
  await page.click('.bcv-nav__item--more[data-extra="help"]');
  await page.waitForSelector('.bcv-sheet-ov .bcv-xrow', { timeout: 10000 });
  check((await texts('.bcv-xrow__t')).join(',') === 'Search the Canvas Guides,IT Help Desk' && /Reporting a problem/.test((await texts('.bcv-sheet__foot'))[0]), `Help lists the school's help links, leaving Canvas-only forms to Canvas: ${(await texts('.bcv-xrow__t')).join(', ')}`);
  await page.keyboard.press('Escape');
  await page.click('.bcv-nav__item--more[data-extra="tool"]');
  await page.waitForSelector('html.bcv-punch #account-tool', { timeout: 10000 });
  check(page.url() === `${BASE}/accounts/1/external_tools/77?launch_type=global_navigation` && (await visible('#bcv-side')), 'an account tool opens Canvas\'s own page for it, with the shell kept over it');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  const navItems = await texts('.bcv-nav .bcv-nav__item');
  check(navItems.length === 8 && navItems[0].startsWith('Dashboard') && /^Notifications( \d+)?$/.test(navItems[5]) && navItems[6].startsWith('Inbox') && navItems[7] === 'Grades', `sidebar nav: ${navItems.join(' | ')}`);
  await waitText('.bcv-nav__item[data-nav="todo"] .bcv-nav__count', /\d/);
  const todoCount = Number((await texts('.bcv-nav__item[data-nav="todo"] .bcv-nav__count'))[0]);
  check(todoCount >= 8, `To Do count in nav = ${todoCount}`);
  check((await texts('.bcv-nav__item[data-nav="inbox"] .bcv-nav__count'))[0] === '1', 'Inbox unread count in nav');
  await page.waitForFunction(() => document.querySelectorAll('.bcv-fav').length === 5, null, { timeout: 10000 });
  check((await texts('.bcv-fav')).length === 5, 'five favourite courses in the sidebar');
  check((await texts('.bcv-account__name'))[0] === 'Sam Student', 'account card shows the user');
  await page.waitForSelector('.bcv-stat', { timeout: 10000 });
  await waitText('.bcv-stat__value', /^\d+$/);
  await page.waitForFunction(() => [...document.querySelectorAll('.bcv-stat__value')].length === 6 && [...document.querySelectorAll('.bcv-stat__value')].every((e) => /^\d+$/.test(e.textContent) && !e.dataset.rolling), null, { timeout: 15000 });
  const stats = await texts('.bcv-stat');
  check(stats.length === 6 && /Due today\s*4\s*35 points total/i.test(stats[0]) && /Due this week/i.test(stats[1]) && /Unread announcements/i.test(stats[2]) && /^Overdue/i.test(stats[3]) && /^Graded this week/i.test(stats[4]) && /^Classes today/i.test(stats[5]), `six stat cards (mockup 13): ${stats.join(' | ')}`);
  check((await page.$eval('.bcv-stats', (e) => getComputedStyle(e).gridTemplateColumns.split(' ').length)) === 3 && (await page.$$eval('.bcv-stat', (els) => els.map((e) => Math.round(e.getBoundingClientRect().top)))).filter((t, i, a) => a.indexOf(t) === i).length === 2, 'a fixed 2×3 grid: three columns, two rows');
  await waitText('.bcv-stats > :nth-child(3) .bcv-stat__value', /^3$/);
  // Overdue: past due with nothing in (Canvas's missing flag) plus late work still without a score; the number matches its sheet
  check(/^Overdue\s*1\s*1 not submitted$/i.test(stats[3]), `Overdue counts work past its due date with nothing in (late work already scored is not overdue): ${stats[3]}`);
  await page.click('.bcv-stats .bcv-stat:nth-child(4)');
  await page.waitForSelector('.bcv-sheet', { timeout: 5000 });
  const overdueRows = await texts('.bcv-sheet__row');
  check((await texts('.bcv-sheet__line'))[0] === '1 Overdue' && /counts as 0 until graded$/.test((await texts('.bcv-sheet__note'))[0]) && overdueRows.length === 1 && /^W2 HW Assignment · 15 pts · due \w+ \d+ · not submitted F26-PHYS 008 01$/.test(overdueRows[0]), `the Overdue sheet lists exactly what it counted: ${overdueRows.join(' | ')}`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet'), null, { timeout: 3000 });
  // Graded this week: graded_at inside the week, points earned over points possible
  check(/^Graded this week\s*\d+\s*(\d+(\.\d+)? \/ \d+(\.\d+)? points|No grades posted this week)$/i.test(stats[4]), `Graded this week with its points ratio: ${stats[4]}`);
  await page.click('.bcv-stats .bcv-stat:nth-child(5)');
  await page.waitForSelector('.bcv-sheet', { timeout: 5000 });
  const gradedRows = await texts('.bcv-sheet__row');
  check(/^\d+ Graded this week$/.test((await texts('.bcv-sheet__line'))[0]) && /week of \w+ \d+$/i.test((await texts('.bcv-sheet__note'))[0]) && gradedRows.length === Number((await texts('.bcv-sheet__value'))[0]) && gradedRows.every((t) => /(\d+(\.\d+)? \/ \d+(\.\d+)?|excused) · posted \w+ \d+/.test(t)), `the Graded sheet: score / possible and the posting day per row: ${gradedRows.slice(0, 2).join(' | ')}`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet'), null, { timeout: 3000 });
  // Classes today: calendar events (never assignments) on the selected courses' calendars
  check(/^Classes today\s*1\s*(Next at 3:00 PM|Last ended 4:15 PM)$/i.test(stats[5]), `Classes today from the course calendars, with the next start or the last end: ${stats[5]}`);
  await page.click('.bcv-stats .bcv-stat:nth-child(6)');
  await page.waitForSelector('.bcv-sheet', { timeout: 5000 });
  const classRows = await texts('.bcv-sheet__row');
  check((await texts('.bcv-sheet__line'))[0] === '1 Classes today' && (await texts('.bcv-sheet__note'))[0] === 'First at 3:00 PM · last ends 4:15 PM' && classRows.length === 1 && /^SPRK 010 seminar Today · 3:00 PM – 4:15 PM F26-SPRK 010 103$/.test(classRows[0]), `the Classes sheet: the event with its hours and course: ${classRows.join(' | ')}`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet'), null, { timeout: 3000 });
  // each counter opens a sheet with the items it counted
  await page.click('.bcv-stats .bcv-stat:first-child');
  await page.waitForSelector('.bcv-sheet', { timeout: 5000 });
  const sheetRows = await texts('.bcv-sheet__row');
  check((await texts('.bcv-sheet__line'))[0] === '4 Due today' && /^35 points across \d courses? · \w+day, \w+ \d+$/.test((await texts('.bcv-sheet__note'))[0]) && sheetRows.length === 4 && sheetRows.some((t) => /^Dis01 Discussion · graded · 10 pts · due 11:59 PM F26-MATH 021 20$/.test(t) || /^Dis01 Assignment · 10 pts · due 11:59 PM F26-MATH 021 20$/.test(t)), `Due today opens its sheet: ${(await texts('.bcv-sheet__note'))[0]} | ${sheetRows.join(' | ')}`);
  await shot(page, '01b-dashboard-sheet');
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet'), null, { timeout: 3000 });
  await page.click('.bcv-stats .bcv-stat:nth-child(2)');
  await page.waitForSelector('.bcv-sheet', { timeout: 5000 });
  // the sheet lists exactly what the counter counted; naming one item here would break every Sunday,
  // when the rest of "this week" is already behind us
  const weekLine = (await texts('.bcv-sheet__line'))[0];
  const weekRows = await texts('.bcv-sheet__row');
  check(/^\d+ Due this week$/.test(weekLine) && Number(weekLine.split(' ')[0]) === weekRows.length && /^Week of \w+ \d+ · \d courses?$/.test((await texts('.bcv-sheet__note'))[0]) && weekRows.every((t) => /·/.test(t)) && weekRows.some((t) => /· \d+ pts ·/.test(t)), `Due this week sheet: ${weekLine} / ${(await texts('.bcv-sheet__note'))[0]} / ${weekRows.length} rows, e.g. ${weekRows[0] || '(none)'}`);
  await page.click('.bcv-sheet-ov', { position: { x: 5, y: 5 } });
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet'), null, { timeout: 3000 });
  await page.click('.bcv-stats .bcv-stat:nth-child(3)');
  await page.waitForSelector('.bcv-sheet', { timeout: 5000 });
  const annRows = await texts('.bcv-sheet__row');
  check((await texts('.bcv-sheet__line'))[0] === '3 Unread announcements' && annRows.length === 3 && /^Field site sign-ups Posted \w+ \d+ · unread F26-SPRK 010 103$/.test(annRows[0]) && !(await page.$('.bcv-sheet__more')), `the number and the rows come from the same list (Announcements API): ${annRows.join(' | ')}`);
  await page.click('.bcv-sheet__row');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(page.url().endsWith('/courses/104/announcements/8005'), 'a sheet row opens the item');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-day .bcv-row', { timeout: 10000 });
  await waitText('.bcv-stats > :nth-child(3) .bcv-stat__value', /^2$/);
  check(true, 'reading an announcement takes it off the count');
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
  // they arrive together rather than one after another (the entrance itself is only on a first draw)
  const cardDelays = await page.$$eval('.bcv-ccard', (els) => els.map((e) => getComputedStyle(e).animationDelay));
  check(cardDelays.length === 5 && cardDelays.every((d) => d === '0s'), `no course card waits its turn: ${cardDelays.join(',')}`);
  await shot(page, '02-dashboard-cards');
  // course colour from the card menu
  await page.click('.bcv-ccard .bcv-ccard__more');
  await page.click('.bcv-menu__item:has-text("Change colour")');
  await page.waitForSelector('.bcv-swatch', { timeout: 3000 });
  check((await page.$$('.bcv-swatch')).length === 16 && (await page.$('.bcv-swatch__input')), 'colour palette: Canvas\'s 15 colours plus a custom one');
  await shot(page, '02b-course-colour');
  await page.click('.bcv-swatch[data-color="#1770AB"]');
  await page.waitForFunction(() => getComputedStyle(document.querySelector('.bcv-ccard__hero')).backgroundColor === 'rgb(23, 112, 171)' && getComputedStyle(document.querySelector('.bcv-fav__dot')).backgroundColor === 'rgb(23, 112, 171)', null, { timeout: 5000 });
  check(await eventually(() => sw.evaluate(async () => (await (await fetch('http://localhost:8787/api/v1/users/self/colors')).text()).includes('"course_101":"#1770AB"'))), 'picking a colour recolours the card and the sidebar at once and saves it to Canvas');
  await page.click('.bcv-seg__btn[data-value="activity"]');
  await page.waitForSelector('.bcv-act__title', { timeout: 5000 });
  const acts = await texts('.bcv-act__kind');
  check(acts.length === 7 && /Announcement · F26-MATH 021 20/.test(acts[0]) && /Discussion · 23 replies/.test(acts[1]), `recent activity: ${acts.slice(0, 2).join(' | ')}`);
  const dots = () => page.$$eval('.bcv-act__dot', (els) => els.filter((e) => getComputedStyle(e).backgroundColor === 'rgb(10, 132, 255)').length);
  check((await dots()) === 3, `unread dots: ${await dots()} (the announcement read a moment ago has none)`);
  await shot(page, '03-dashboard-activity');
  check(await sw.evaluate(async () => (await fetch('http://localhost:8787/dashboard/view').then((r) => r.text())).includes('activity')), 'dashboard view persisted to Canvas');
  await (await page.$$('.bcv-body .bcv-row--top'))[1].click();
  await page.waitForSelector('.bcv-entry', { timeout: 10000 });
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-act__title', { timeout: 10000 });
  // the announcement's own read state arrives with the feed, a moment after the list is drawn
  check(await eventually(async () => (await dots()) === 2), `opening a stream item clears its dot: ${await dots()} left`);
  await page.click('.bcv-seg__btn[data-value="list"]');

  // ---- courses ----------------------------------------------------------------------------
  console.log('courses');
  await page.evaluate(() => { window.__bcvSpa = 1; }); // survives only if the page is not reloaded
  await nav('courses');
  await page.waitForSelector('.bcv-ccard__hero--term', { timeout: 10000 });
  check(page.url() === `${BASE}/courses` && (await page.evaluate(() => window.__bcvSpa === 1)) && (await page.$eval('.bcv-nav__item[data-nav="courses"]', (e) => e.classList.contains('is-active'))), 'sidebar navigation moves the address and draws the screen in place, no reload; the sidebar follows');
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
  // a nickname, Canvas's own: the pencil on a card opens a one-field sheet; the card, the sidebar and Canvas follow
  const coursesJson = () => fetch(`${BASE}/api/v1/courses?per_page=100`).then((r) => r.text()).then((t) => JSON.parse(t.replace(/^while\(1\);/, '')));
  const mathCard = page.locator('.bcv-ccard', { hasText: 'F26-MATH 021 20' }).first();
  await mathCard.hover();
  await mathCard.locator('.bcv-ccard__nick').click();
  await page.waitForSelector('.bcv-sheet--prompt', { timeout: 5000 });
  check((await texts('.bcv-sheet--prompt .bcv-sheet__title'))[0] === 'Nickname' && !(await page.$('.bcv-prompt__clear')) && (await page.$eval('.bcv-prompt__input', (e) => e.placeholder)) === 'F26-MATH 021 20', 'the pencil on a card opens the nickname sheet, the real name as the placeholder, nothing to remove yet');
  await page.fill('.bcv-prompt__input', 'Calc');
  await page.click('.bcv-prompt__save');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet--prompt') && [...document.querySelectorAll('.bcv-ccard__code')].some((e) => e.textContent.trim() === 'Calc'), null, { timeout: 10000 });
  const nicked = (await coursesJson()).find((c) => String(c.id) === '101');
  check(nicked.name === 'Calc' && nicked.original_name === 'F26-MATH 021 20' && (await texts('.bcv-fav')).includes('Calc'), `Save writes the nickname to Canvas (name Calc, the real name kept as original_name) and the sidebar follows: ${(await texts('.bcv-fav')).join(', ')}`);
  await shot(page, '04b-courses-nickname');
  const calcCard = page.locator('.bcv-ccard', { hasText: 'Calc' }).first();
  await calcCard.hover();
  await calcCard.locator('.bcv-ccard__nick').click();
  await page.waitForSelector('.bcv-prompt__clear', { timeout: 5000 });
  await page.click('.bcv-prompt__clear');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet--prompt') && [...document.querySelectorAll('.bcv-ccard__code')].some((e) => e.textContent.trim() === 'F26-MATH 021 20'), null, { timeout: 10000 });
  check(!(await coursesJson()).find((c) => String(c.id) === '101').original_name && (await page.$$('.bcv-row .bcv-iconbtn')).length > 0, 'Remove nickname restores the real name in Canvas; the non-favourite rows carry a pencil too');
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
  check(await eventually(() => sw.evaluate(async () => (await (await fetch('http://localhost:8787/api/v1/courses')).text()).includes('"is_favorite":true'))), 'the card moved at once; the favourite is saved to Canvas in the background');
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
  // ---- mockup 12: a task of your own (a Canvas planner note), priority on every row, By priority ----
  console.log('to do: own tasks + priority');
  const badgeBefore = Number((await texts('.bcv-nav__item[data-nav="todo"] .bcv-nav__count'))[0]);
  const subBefore = (await texts('.bcv-head__sub'))[0];
  check(!!(await page.$('.bcv-todo__add')) && !(await page.$('.bcv-todo__del')), 'an "Add your own task" row sits under the list; Canvas rows have no delete button');
  await page.click('.bcv-todo__add');
  await page.waitForSelector('.bcv-todo__composer', { timeout: 5000 });
  check(!!(await page.$('.bcv-todo__addbtn[disabled]')) && /^Today · (Sun|Mon|Tue|Wed|Thu|Fri|Sat), \w{3} \d+$/.test((await texts('.bcv-todo__date'))[0]) && !(await page.$('.bcv-todo__ctl .bcv-seg')) && !(await page.$('input[type="date"]')), `the composer: Add is blocked while the title is empty; the date field starts at today (${(await texts('.bcv-todo__date'))[0]}), no segment, no browser date input`);
  await page.fill('.bcv-todo__title', 'Email Prof. Lei about office hours');
  await page.click('.bcv-todo__date');
  await page.waitForSelector('.bcv-datepop', { timeout: 3000 });
  const popMonth = (await texts('.bcv-datepop__month'))[0];
  check((await page.$$('.bcv-datepop__day')).length === 42 && (await page.$eval('.bcv-datepop', (e) => e.scrollHeight <= e.clientHeight + 1)) && (await page.$$('.bcv-datepop__day.is-today')).length === 1 && (await page.$$('.bcv-datepop__day.is-on.is-today')).length === 1 && (await texts('.bcv-datepop__q')).join(',') === 'Today,Tomorrow,Next Monday' && new Date().toLocaleString('en-US', { month: 'long' }) === popMonth, `the field opens the app's own calendar on this month (${popMonth}): six weeks, today marked and chosen, Today / Tomorrow / Next Monday shortcuts`);
  await page.click('.bcv-datepop__nav[aria-label="Next month"]');
  check(!!(await page.$('.bcv-datepop')) && (await texts('.bcv-datepop__month'))[0] !== popMonth && (await page.$$('.bcv-datepop__day.is-today')).length === 0, `› moves a month on without closing the calendar (${(await texts('.bcv-datepop__month'))[0]})`);
  await page.click('.bcv-datepop__q:nth-child(2)'); // Tomorrow
  await page.waitForFunction(() => !document.querySelector('.bcv-datepop'), null, { timeout: 3000 });
  check(/^Tomorrow · /.test((await texts('.bcv-todo__date'))[0]), `picking a day closes the calendar and the field says it the way people do: ${(await texts('.bcv-todo__date'))[0]}`);
  await page.click('.bcv-todo__date');
  await page.waitForSelector('.bcv-datepop', { timeout: 3000 });
  check((await page.$$('.bcv-datepop__day.is-on')).length === 1 && (await page.$eval('.bcv-datepop__day.is-on', (e) => !e.classList.contains('is-today'))), 'reopened, the calendar shows the chosen day filled');
  await page.click('.bcv-datepop__q:nth-child(1)'); // back to Today
  await page.waitForFunction(() => !document.querySelector('.bcv-datepop'), null, { timeout: 3000 });
  check(!(await page.$('.bcv-todo__addbtn[disabled]')) && (await texts('.bcv-pri--draft'))[0] === 'Medium', 'with a title Add unlocks; the draft priority starts at Medium');
  await page.click('.bcv-pri--draft');
  await page.waitForSelector('.bcv-menu', { timeout: 3000 });
  check((await texts('.bcv-menu__item')).join(',') === 'High,Medium,Low,None' && (await page.$$('.bcv-menu .bcv-dot')).length === 4, `the priority menu lists the four levels with a dot each: ${(await texts('.bcv-menu__item')).join(',')}`);
  await page.click('.bcv-menu__item:first-child');
  await page.waitForFunction(() => !document.querySelector('.bcv-menu'), null, { timeout: 3000 });
  check((await texts('.bcv-pri--draft'))[0] === 'High', 'picking High sets the draft chip');
  await page.click('.bcv-todo__addbtn');
  await page.waitForFunction(() => [...document.querySelectorAll('.bcv-group__head')].some((e) => /^My tasks/.test(e.textContent)), null, { timeout: 10000 });
  const noteRow = page.locator('.bcv-row', { hasText: 'Email Prof. Lei about office hours' }).first();
  const noteText = (await noteRow.innerText()).replace(/\s+/g, ' ');
  const notes = await fetch(`${BASE}/api/v1/planner_notes`).then((r) => r.text()).then((t) => JSON.parse(t.replace(/^while\(1\);/, '')));
  check(notes.length === 1 && notes[0].title === 'Email Prof. Lei about office hours' && /T\d\d:\d\d/.test(notes[0].todo_date) && !('priority' in notes[0]), `the task is a Canvas planner note with today as its todo_date, and no priority ever reaches Canvas: ${JSON.stringify(notes[0])}`);
  check(/My task/.test(noteText) && /High/.test(noteText) && /11:59 PM|Today/.test(noteText) && (await noteRow.locator('.bcv-todo__del').count()) === 1 && (await page.$$('.bcv-todo__del')).length === 1 && !(await noteRow.locator('.bcv-btn--xs').count()), `the row: My task, High, due today, the only row with a delete button and no Submit: ${noteText}`);
  const subAfter = (await texts('.bcv-head__sub'))[0];
  await page.waitForFunction((n) => Number(document.querySelector('.bcv-nav__item[data-nav="todo"] .bcv-nav__count').textContent) === n + 1, badgeBefore, { timeout: 5000 });
  const subM = subBefore.match(/^(\d+) items across (\d+) courses$/);
  check(!!subM && subAfter === `${Number(subM[1]) + 1} items across ${subM[2]} courses · one of your own`, `the header counts the task but not as a course, and the badge follows the same list: ${subBefore} → ${subAfter}`);
  // a Canvas assignment gets a priority too; it survives a reload, keyed by the item's id
  const firstWork = page.locator('.bcv-row', { hasNot: page.locator('.bcv-todo__del') }).filter({ has: page.locator('.bcv-pri') }).first();
  const workId = await firstWork.getAttribute('data-item');
  await firstWork.locator('.bcv-pri').click();
  await page.waitForSelector('.bcv-menu', { timeout: 3000 });
  await page.click('.bcv-menu__item:nth-child(3)'); // Low
  await page.waitForFunction((id) => document.querySelector(`.bcv-row[data-item="${id}"] .bcv-pri`)?.textContent.trim() === 'Low', workId, { timeout: 5000 });
  await page.reload();
  await page.waitForSelector('.bcv-row[data-item] .bcv-pri', { timeout: 10000 });
  check((await page.$eval(`.bcv-row[data-item="${workId}"] .bcv-pri`, (e) => e.textContent.trim())) === 'Low' && /^(assignment|quiz|discussion_topic|wiki_page|calendar_event):/.test(workId), `a Canvas item's priority is kept by its stable id and survives a reload: ${workId} → Low`);
  const prefPri = (await sw.evaluate(async () => Object.entries(await chrome.storage.local.get(null)).find(([k]) => k.startsWith('prefs:'))?.[1]?.todoPriority));
  check(prefPri && prefPri[workId] === 1 && Object.values(prefPri).includes(3), `priority lives in the site's preferences, never in Canvas: ${JSON.stringify(prefPri)}`);
  // By priority buckets High → Low → Unprioritised and drops the empty Medium bucket
  await page.click('.bcv-head .bcv-seg__btn:nth-child(2)');
  await page.waitForFunction(() => /^High priority/.test(document.querySelector('.bcv-group__head')?.textContent || ''), null, { timeout: 5000 });
  const priHeads = (await texts('.bcv-group__head')).map((t) => (t.match(/^(High priority|Medium priority|Low priority|Unprioritised)/) || ['?'])[0]);
  check(priHeads.join(' | ') === 'High priority | Low priority | Unprioritised', `By priority: ${priHeads.join(' | ')}`);
  await page.click('.bcv-head .bcv-seg__btn:nth-child(1)');
  await page.waitForFunction(() => /^My tasks/.test(document.querySelector('.bcv-group__head')?.textContent || ''), null, { timeout: 5000 });
  // delete: only a task of your own, after a confirmation; the note leaves Canvas and the badge drops back
  page.once('dialog', (d) => d.accept());
  await page.click('.bcv-todo__del');
  await page.waitForFunction(() => ![...document.querySelectorAll('.bcv-group__head')].some((e) => /^My tasks/.test(e.textContent)), null, { timeout: 10000 });
  await page.waitForFunction((n) => Number(document.querySelector('.bcv-nav__item[data-nav="todo"] .bcv-nav__count').textContent) === n, badgeBefore, { timeout: 5000 });
  check((await fetch(`${BASE}/api/v1/planner_notes`).then((r) => r.text()).then((t) => JSON.parse(t.replace(/^while\(1\);/, '')))).length === 0 && !(await page.$('.bcv-todo__del')), 'Delete removes the planner note from Canvas; the list and the badge agree again');
  check(true, 'ticking the circle marks an item complete');
  check((await page.$eval('.bcv-body > :first-child', (e) => e.className)).includes('bcv-todo__add') && /^Show completed · \d+$/.test((await texts('.bcv-todo__done'))[0]), `Add your own task leads the page; a small header button shows completed items with their count: ${(await texts('.bcv-todo__done'))[0]}`);
  await page.click('.bcv-todo__done');
  await page.waitForSelector('.bcv-body .bcv-row--done', { timeout: 5000 });
  check((await texts('.bcv-todo__done'))[0] === 'Hide completed', 'the button flips to Hide completed');
  const doneRows = await texts('.bcv-body .bcv-row--done');
  check(doneRows.length >= 3 && doneRows.some((t) => /Dismissed.*Restore/.test(t)), `show completed reveals ${doneRows.length} done/dismissed rows with Restore`);
  await page.click('.bcv-body .bcv-row--done .bcv-circle');
  await page.waitForFunction((n) => document.querySelectorAll('.bcv-body .bcv-row--done').length === n - 1, doneRows.length, { timeout: 5000 });
  check(true, 'unticking a completed item reopens it');
  await page.click('.bcv-todo__done');
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
  const ownCals = await texts('.bcv-cal__own .bcv-calrow__name');
  const otherCals = await texts('.bcv-cal__other .bcv-calrow__name');
  check(cals.length === 11 && ownCals.length === 5 && !ownCals.includes('Sam Student') && otherCals[0] === 'Sam Student' && otherCals.includes('Placement Exam: Chemistry') && otherCals.includes('Attestation Fall 2026 1')
    && (await page.$$('.bcv-cal__own .bcv-switch.is-on')).length === 5 && (await page.$$('.bcv-cal__other .bcv-switch.is-on')).length === 0,
  `the favourite courses are the calendars on by default; the personal calendar, other courses and groups wait under Other calendars, off: ${ownCals.join(', ')} | other: ${otherCals.join(', ')}`);
  check(!evs.some((t) => /Chemistry placement/.test(t)) && !(await page.$('.bcv-cal__notice')), 'nothing from an Other calendar is shown, and nothing is said about it, until it is turned on');
  // turning on a calendar Canvas refuses (401): it is retried alone, marked, and the rest still load
  await (await page.$$('.bcv-cal__other .bcv-switch'))[otherCals.indexOf('Placement Exam: Chemistry')].click();
  await page.waitForSelector('.bcv-calrow--refused', { timeout: 10000 });
  check((await texts('.bcv-calrow--refused .bcv-calrow__name'))[0] === 'Placement Exam: Chemistry' && /would not share one calendar \(Placement Exam: Chemistry\)/.test((await texts('.bcv-cal__notice'))[0]) && !(await texts('.bcv-ev')).some((t) => /Chemistry placement/.test(t)) && (await texts('.bcv-ev')).length === evs.length, 'a calendar Canvas refuses (401) is retried alone, marked "Not shared", and the rest still load');
  await shot(page, '06-calendar-month');
  await (await page.$$('.bcv-cal__own .bcv-switch'))[0].click(); // the first favourite course off…
  await page.waitForTimeout(400);
  check((await texts('.bcv-ev')).length < evs.length, 'switching a calendar off hides its events');
  await (await page.$$('.bcv-cal__own .bcv-switch'))[0].click(); // …and on again: the week and agenda checks below read its events
  await page.waitForFunction((n) => document.querySelectorAll('.bcv-ev').length >= n, evs.length, { timeout: 10000 });
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
  // The month either side is already warmed and answers from the memo, so the first Next says
  // nothing about a failing calendar API; the second lands on a month nothing could warm (its own
  // warm failed too), which is the one that has to ask Canvas and be refused.
  await page.click('.bcv-cal__nav .bcv-iconbtn:nth-child(2)');
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

  // ---- grades panel (sidebar Grades) --------------------------------------------------------
  console.log('grades panel');
  await nav('gpa');
  await page.waitForSelector('.bcv-gpa__value', { timeout: 10000 });
  check(page.url() === `${BASE}/grades` && (await texts('.bcv-h1'))[0] === 'Grades' && (await texts('.bcv-head__sub'))[0] === 'Fall 2026 · 5 courses · 4 with grades so far', `grades panel header: ${(await texts('.bcv-head__sub'))[0]}`);
  check(/^3\.4[23]$/.test((await texts('.bcv-gpa__value'))[0]) && (await page.$('.bcv-gpa__banner')) && /needs your past record/.test((await texts('.bcv-gpa__hero'))[0]) && /No history yet/.test((await texts('.bcv-gpa__trend'))[0]), 'term GPA is the plain 4.0 average of the four scored courses; cumulative and trend wait for setup');
  const gpaStats = await texts('.bcv-gpa__stat');
  check(gpaStats.length === 3 && /^Momentum.*Turn on tracking to compare snapshots$/i.test(gpaStats[0]) && /On-time submissions \d+% \d+ of \d+ submitted before the due time/i.test(gpaStats[1]) && (await page.$$('.bcv-gpa__chip')).length === 4 && /Highest .* at \d+% · lowest .* at \d+%/.test(gpaStats[2]), `stats: ${gpaStats.join(' | ')}`);
  const gpaCards = await texts('.bcv-gpa__card');
  check(gpaCards.length === 5 && /^A− F26-MATH 021 20 MATH-021-20 92\.4% 3\.7 pts Needs \d+% of the remaining 507 pts 93 pts earned so far Target A− Details$/.test(gpaCards[0]) && gpaCards.filter((t) => /^N\/A .*N\/A — pts Nothing graded yet — no score to project from .*No grade yet Details$/.test(t)).length === 1, `course cards: ${gpaCards[0]} || ${gpaCards[4]}`);
  // hovering the ring alone opens the group breakdown in place; the card keeps its size
  const cardHeight = await page.$eval('.bcv-gpa__card', (el) => el.getBoundingClientRect().height);
  const ringAtRest = await page.$eval('.bcv-gpa__card .bcv-gpa__ringsvg', (el) => Math.round(el.getBoundingClientRect().width));
  await page.hover('.bcv-gpa__card .bcv-gpa__ringbox');
  await waitText('.bcv-gpa__card', /By group/); // waitText reads textContent (source case) and keeps no regex flags
  const ringHovered = await page.$eval('.bcv-gpa__card.is-hover .bcv-gpa__ringsvg', (el) => Math.round(el.getBoundingClientRect().width));
  check(ringAtRest === 82 && ringHovered === 82, `the course ring is one size at rest and expanded (the group rings nest inside it): ${ringAtRest}px → ${ringHovered}px`);
  const hoverCard = (await texts('.bcv-gpa__card'))[0];
  check(/^by group 92\.4% .*Discussion Quizzes \d+%/i.test(hoverCard) && (await page.$$('.bcv-gpa__card.is-hover .bcv-gpa__ringsvg circle')).length >= 6 && Math.abs((await page.$eval('.bcv-gpa__card', (el) => el.getBoundingClientRect().height)) - cardHeight) < 1, `hovering the ring shows the group rings + breakdown without resizing the card: ${hoverCard}`);
  await page.mouse.move(5, 5);
  await page.waitForFunction(() => !document.querySelector('.bcv-gpa__card.is-hover'), null, { timeout: 5000 });
  await shot(page, '09d-grades-panel');
  // Details: the course's grade page in a sheet, with the target stepper
  await page.click('.bcv-gpa__card .bcv-gpa__details');
  await page.waitForSelector('.bcv-gpa-detail', { timeout: 5000 });
  const detail = (await texts('.bcv-gpa-detail'))[0];
  check(/^F26-MATH 021 20 MATH-021-20 92\.4% A− Target − A− · 90% \+ by group/i.test(detail) && /how the grade is weighted 100% of final grade/i.test(detail) && /Midterms 57% · nothing graded/.test(detail) && (await page.$$('.bcv-gpa-detail__arow')).length === 17 && (await page.$$('.bcv-gpa-detail .bcv-wbar__seg--ungraded')).length === 2, `details sheet: ${detail.slice(0, 200)}`);
  await page.click('.bcv-gpa-detail__step:last-child');
  await waitText('.bcv-gpa-detail__tval', /^A · 93%$/);
  check(/Needs 9\d% of the remaining 507 pts .*Target A Details$/.test((await texts('.bcv-gpa__card'))[0]), `a target stepped in the sheet updates the card behind it: ${(await texts('.bcv-gpa__card'))[0]}`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet-ov'), null, { timeout: 5000 });
  // hiding a course drops it from the GPA, reversibly, and the page says so
  await page.click('.bcv-gpa__card .bcv-gpa__hide');
  await page.waitForSelector('.bcv-gpa__tray', { timeout: 5000 });
  check((await texts('.bcv-gpa__value'))[0] === '3.33' && (await texts('.bcv-head__sub'))[0] === 'Fall 2026 · 4 courses · 3 with grades so far' && /hidden · not counted in gpa/i.test((await texts('.bcv-gpa__tray'))[0]) && (await texts('.bcv-gpa__traychip'))[0] === 'F26-MATH 021 20 Show' && (await page.$$('.bcv-gpa__card')).length === 4, `a hidden course leaves the GPA and waits in the tray: ${(await texts('.bcv-gpa__value'))[0]} · ${(await texts('.bcv-head__sub'))[0]}`);
  await page.click('.bcv-gpa__traychip');
  await waitText('.bcv-gpa__value', /^3\.4[23]$/);
  check(!(await page.$('.bcv-gpa__tray')) && (await page.$$('.bcv-gpa__card')).length === 5, 'Show returns it to the overview and the GPA');
  await page.click('.bcv-gpa__banner .bcv-btn');
  await page.waitForSelector('.bcv-gpa-set', { timeout: 5000 });
  await page.fill('#bcv-gpa-prior', '3.42');
  await page.fill('#bcv-gpa-prior-n', '8');
  await page.click('.bcv-gpa-set__ctl .bcv-gpa-set__step:last-child'); // + at the 4.00 default: already at the top of the scale
  check((await texts('.bcv-gpa-set__val'))[0] === '4.00', `the goal does not climb past 4.00: ${(await texts('.bcv-gpa-set__val'))[0]}`);
  await page.click('.bcv-gpa-set__ctl .bcv-gpa-set__step:first-child');
  check((await texts('.bcv-gpa-set__val'))[0] === '3.95', 'goal steps by 0.05 in the settings sheet');
  await page.click('.bcv-gpa-set__foot .bcv-btn');
  await page.waitForFunction(() => !document.querySelector('.bcv-gpa__banner'), null, { timeout: 5000 });
  const hero = (await texts('.bcv-gpa__hero'))[0];
  check(/Cumulative 3\.4[23]/.test(hero) && /3\.42 across 8 courses before this term/.test(hero) && /Goal 3\.95/.test(hero) && /One snapshot so far/.test((await texts('.bcv-gpa__trend'))[0]) && (await texts('.bcv-gpa__stat'))[0].includes('Needs a second snapshot'), `tracking on: ${hero.slice(0, 120)}`);
  // prefs live in chrome.storage.local under `prefs:<canvas host>`; the first snapshot lands the day tracking starts
  const readPrefs = () => sw.evaluate(async () => Object.entries(await chrome.storage.local.get(null)).find(([k]) => k.startsWith('prefs:'))?.[1] || null);
  check(await eventually(async () => { const p = await readPrefs(); return Array.isArray(p?.gpaSnapshots) && p.gpaSnapshots.length === 1 && typeof p.gpaSnapshots[0].gpa === 'number' && /^\d{4}-\d{2}-\d{2}$/.test(p.gpaSnapshots[0].date) && p.gpaTracking?.priorCourses === 8 && p.gpaGoal === 3.95; }), `a first snapshot is recorded the day tracking starts: ${JSON.stringify((await readPrefs())?.gpaSnapshots)}`);
  await shot(page, '09e-grades-panel-tracking');
  page.once('dialog', (d) => d.accept());
  await page.click('.bcv-gpa__linkbtn');
  await page.waitForSelector('.bcv-gpa__banner', { timeout: 5000 });
  check(await eventually(async () => { const p = await readPrefs(); return p?.gpaTracking === null && Array.isArray(p?.gpaSnapshots) && p.gpaSnapshots.length === 0 && p.gpaGoal === 3.95; }) && /needs your past record/.test((await texts('.bcv-gpa__hero'))[0]), 'Reset setup forgets the prior record and snapshots but keeps the goal');

  // ---- handing work in (assignment submission flow) -------------------------------------
  console.log('submission');
  const readSub = (cid, aid) => fetch(`${BASE}/api/v1/courses/${cid}/assignments/${aid}/submissions/self`).then((r) => r.text()).then((t) => JSON.parse(t.replace(/^while\(1\);/, '')));
  await page.goto(`${BASE}/courses/104/assignments/4002`);
  await page.waitForSelector('.bcv-detail__actions .bcv-btn--primary', { timeout: 10000 });
  check((await texts('.bcv-detail__actions .bcv-btn--primary'))[0] === 'Submit assignment', 'the assignment page offers our own submit flow');
  // mockup 11: the flow is a block at the end of the assignment page itself, not a destination of its own
  const inView = (sel) => page.$eval(sel, (el) => { const r = el.getBoundingClientRect(); return r.top >= 0 && r.top < window.innerHeight; });
  check(!!(await page.$('#bcv-main .bcv-detail + .bcv-sb--embed, #bcv-main .bcv-sb--embed')) && (await page.$$('.bcv-sb__foot')).length === 1 && !(await page.$('.bcv-sb__h1')) && (await page.$eval('.bcv-sb__foot', (el) => getComputedStyle(el).position)) === 'static', 'handing in is a block under the instructions in the same scroll, with a plain (not sticky) submit row');
  await page.click('.bcv-detail__actions .bcv-btn--primary');
  check(await eventually(() => inView('#bcv-submit')) && page.url() === `${BASE}/courses/104/assignments/4002` && (await texts('.bcv-detail__title'))[0] === 'Week 2 Post Class Assignment: GC articles' && (await visible('#bcv-side')), 'Submit assignment brings the block into view on the same page (sidebar kept, no navigation)');
  const subChips = await texts('.bcv-sb__chip');
  check(subChips.length === 3 && /^[A-Z][a-z]+ by 11:59 PM$/.test(subChips[0]) && subChips[1] === '10 points' && subChips[2] === 'Attempt 1 of unlimited' && (await texts('.bcv-sb__kicker'))[0].toLowerCase() === 'hand in', `block chips: ${subChips.join(' | ')}`);
  check(/^Open [A-Z][a-z]{2} \d+ – [A-Z][a-z]{2} \d+ · accepts a file upload, a text entry or a website URL$/.test((await texts('.bcv-sb__note'))[0]), `availability + accepted types: ${(await texts('.bcv-sb__note'))[0]}`);
  check((await texts('.bcv-sb__dropsub'))[0] === 'PDF, DOCX, PNG or JPG only · as many files as you need' && (await page.getAttribute('.bcv-sb__pane input[type=file]', 'accept')) === '.pdf,.docx,.png,.jpg', 'allowed file types are read from the assignment and set the picker\'s accept');
  check((await texts('.bcv-sb__footnote'))[0] === 'Attach at least one file to submit.' && !!(await page.$('.bcv-sb__btn--primary[disabled]')), 'submit stays blocked until a file is attached');
  await page.setInputFiles('.bcv-sb__pane input[type=file]', { name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from('hello') });
  await waitText('.bcv-toast', /notes\.txt.*isn't an accepted type.*PDF, DOCX, PNG or JPG/);
  check(/^nothing attached yet$/i.test((await texts('.bcv-sb__count'))[0]), `a forbidden type is refused before any upload, with the reason: ${(await texts('.bcv-sb__count'))[0]}`); // innerText carries the CSS uppercase
  await page.setInputFiles('.bcv-sb__pane input[type=file]', { name: 'grand-challenge-notes.docx', mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', buffer: Buffer.alloc(38912, 'a') });
  await waitText('.bcv-sb__count', /^1 file attached$/);
  check((await texts('.bcv-sb__file'))[0].replace(/\s+/g, ' ') === 'DOCX grand-challenge-notes.docx 38 KB · ready to submit', `file row: ${(await texts('.bcv-sb__file'))[0].replace(/\s+/g, ' ')}`);
  check(/anything after 11:59 PM is marked late/.test((await texts('.bcv-sb__footnote'))[0]) && !(await page.$('.bcv-sb__btn--primary[disabled]')), 'submit unlocks with a file and the note says when late starts');
  // Other: the tool's own picker, punched through in a sheet; what it hands back joins the list
  await page.click('.bcv-sb__tab[data-tab=other]');
  check((await texts('.bcv-sb__toolname')).join(' | ') === 'Box | Office 365 | Website URL', `Other rows: ${(await texts('.bcv-sb__toolname')).join(' | ')}`);
  await page.click('.bcv-sb__tool');
  await page.waitForSelector('.bcv-sheet--tool .bcv-sb__frame', { timeout: 5000 });
  check((await page.frameLocator('.bcv-sb__frame').locator('#tool-title').innerText()) === "Box picker (the tool's own page)" && !(await page.frameLocator('.bcv-sb__frame').locator('#bcv-app').count()), 'the tool\'s page is framed untouched (no skin inside the frame)');
  await page.frameLocator('.bcv-sb__frame').locator('#pick').click();
  await waitText('.bcv-sb__count', /^2 files attached$/);
  check(!(await page.$('.bcv-sheet--tool')) && (await texts('.bcv-sb__file'))[1].replace(/\s+/g, ' ') === 'PDF GC-articles-Sharma.pdf from Box · ready to submit', `the file the tool handed back joins the list: ${(await texts('.bcv-sb__file'))[1].replace(/\s+/g, ' ')}`);
  await page.fill('.bcv-sb__comment', 'Three sources, APA.');
  await page.click('.bcv-sb__btn--primary');
  await page.waitForSelector('.bcv-sb__done', { timeout: 15000 });
  const receipt = (await texts('.bcv-sb__rrow')).map((t) => t.replace(/\s+/g, ' '));
  check(/^Submitted [A-Z][a-z]{2} \d+ at \d+:\d\d [AP]M$/.test(receipt[0]) && receipt[1] === 'Submission grand-challenge-notes.docx, GC-articles-Sharma.pdf' && /^Turned in \d+ (minutes?|hours?) before the deadline$/.test(receipt[2]) && receipt[3] === 'Attempt 1' && receipt[4] === 'Grade Not graded yet', `receipt: ${receipt.join(' | ')}`);
  const sub1 = await readSub('104', '4002');
  check(sub1.workflow_state === 'submitted' && sub1.attempt === 1 && sub1.submission_type === 'online_upload' && sub1.attachments.length === 2 && sub1.attachments[0].display_name === 'grand-challenge-notes.docx' && sub1.attachments[0].size === 38912 && sub1.attachments[1].from_url === `${BASE}/files/box1/download` && sub1.submission_comments.some((c) => c.comment === 'Three sources, APA.'), `Canvas holds the upload, the tool's file and the comment: ${JSON.stringify(sub1.attachments.map((f) => [f.display_name, f.size]))}`);
  // resubmit as a text entry; the draft lives on this device until it is sent
  await page.click('.bcv-sb__donebtns .bcv-sb__btn:not(.bcv-sb__btn--primary)');
  await page.waitForSelector('.bcv-sb__tabs', { timeout: 5000 });
  check((await texts('.bcv-sb__chip'))[2] === 'Attempt 2 of unlimited' && /^nothing attached yet$/i.test((await texts('.bcv-sb__count'))[0]), `Resubmit starts the next attempt with an empty list: ${(await texts('.bcv-sb__chip'))[2]} · ${(await texts('.bcv-sb__count'))[0]}`);
  await page.click('.bcv-sb__tab[data-tab=text]');
  await page.fill('.bcv-sb__ta', 'Clean water for all.\n\nThree sources follow.');
  await page.waitForTimeout(700);
  await page.reload();
  await page.waitForSelector('.bcv-sb__foot', { timeout: 10000 });
  check(!!(await page.$('.bcv-sb__tab[data-tab=text].is-active')) && !!(await page.$('.bcv-sb__ta')), 'the tab chosen is remembered for this assignment (a text-entry assignment never reopens on the file tab)');
  check((await page.inputValue('.bcv-sb__ta')) === 'Clean water for all.\n\nThree sources follow.' && /Draft restored/.test((await texts('.bcv-sb__note'))[1]), 'the text entry survives a reload as a draft on this device');
  await page.click('.bcv-sb__btn--primary');
  await page.waitForSelector('.bcv-sb__done', { timeout: 15000 });
  const sub2 = await readSub('104', '4002');
  check(sub2.attempt === 2 && sub2.submission_type === 'online_text_entry' && sub2.body === '<p>Clean water for all.</p><p>Three sources follow.</p>' && (await texts('.bcv-sb__rrow'))[1].replace(/\s+/g, ' ') === 'Submission Text entry', `the text entry is recorded as HTML paragraphs: ${sub2.body}`);
  await shot(page, '09f-submitted');
  check((await texts('.bcv-sb__donebtns .bcv-sb__btn--primary'))[0] === 'Done', 'the receipt on the assignment page ends with Done (nowhere else to go back to)');
  await page.click('.bcv-sb__donebtns .bcv-sb__btn--primary');
  await page.waitForFunction(() => !document.querySelector('.bcv-sb__done'), null, { timeout: 10000 });
  await page.waitForSelector('.bcv-detail__actions .bcv-btn--primary', { timeout: 10000 });
  check(page.url() === `${BASE}/courses/104/assignments/4002` && (await texts('.bcv-detail__actions .bcv-btn--primary'))[0] === 'Resubmit' && (await texts('.bcv-badge')).includes('Submitted'), 'Done reloads the assignment page: status Submitted, button Resubmit');
  // To Do rows land on the block and the page's back link returns there
  await nav('todo');
  await page.waitForSelector('.bcv-row', { timeout: 10000 });
  await page.locator('.bcv-row', { hasText: 'Research Day Activity' }).first().locator('.bcv-btn--xs', { hasText: 'Submit' }).click();
  await page.waitForSelector('.bcv-sb__foot', { timeout: 10000 });
  const fromTodo = { url: page.url(), back: (await texts('.bcv-cmain .bcv-linkbtn'))[0]?.trim(), inView: await eventually(() => inView('#bcv-submit')), drop: (await texts('.bcv-sb__dropsub'))[0], tabs: (await texts('.bcv-sb__tab')).join(' | ') };
  check(fromTodo.url === `${BASE}/courses/105/assignments/5002?bcv=submit&from=todo` && fromTodo.back === 'To Do' && fromTodo.inView && fromTodo.drop.startsWith('Any file type') && fromTodo.tabs === 'File upload | Text entry', `a To Do row opens the assignment scrolled to the block, with To Do as the way back; no Other tab when the course has no tools: ${JSON.stringify(fromTodo)}`);
  await shot(page, '09g-submit-from-todo');

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
  // Between a group's tabs the header and the rail stay put, as they do within a course: the same
  // nodes are still there afterwards, so a tab is a redraw of one column, not of the whole screen.
  await page.$eval('.bcv-head h1', (el) => { el.dataset.bcvKeepMark = 'yes'; });
  await page.$eval('.bcv-rail__item[data-tab="people"]', (el) => { el.dataset.bcvKeepMark = 'yes'; });
  await tab('people');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  check((await texts('.bcv-body .bcv-row')).length === 2 && (await texts('.bcv-badge')).includes('Member'), 'group people');
  const keptMarks = await page.$$eval('[data-bcv-keep-mark]', (els) => els.map((e) => e.className));
  check(keptMarks.length === 2 && (await page.$eval('.bcv-rail__item[data-tab="people"]', (el) => el.classList.contains('is-active'))), `a group's header and rail survive a tab, the new tab marked active: ${keptMarks.length} kept`);

  // /api/v1/courses is the slowest call the app makes, and a group row wants its course only for a
  // subtitle and a badge: the rows are drawn from /users/self/groups alone and the courses fold in
  // behind them. Held back once here, so the gap is long enough to be caught in the act. A real
  // page load, not an in-place hop, because that is what starts with an empty memo.
  let holdCourses = true;
  await page.route(/\/api\/v1\/courses\?/, async (route) => {
    if (!holdCourses) { await route.continue().catch(() => {}); return; }
    holdCourses = false; // steps aside on its own; unrouting would race the request it is holding
    await new Promise((r) => setTimeout(r, 1500));
    await route.continue().catch(() => {});
  });
  await page.goto(`${BASE}/groups`, { waitUntil: 'commit' });
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  const early = await texts('.bcv-body .bcv-row');
  check(early.length >= 2 && /Attestation Fall 2026 1/.test(early[0]) && !/Academic Success/.test(early.join(' ')), `the groups are listed without waiting for the course list: ${early[0]}`);
  await page.waitForFunction(() => /Academic Success/.test(document.querySelector('.bcv-body')?.textContent || ''), null, { timeout: 10000 });
  const settled = (await texts('.bcv-body > div > .bcv-label')).join(',').toLowerCase();
  check(settled === 'current groups,previous groups', `the courses fold in behind them, and Previous groups appears once a group's course can be called past: ${settled}`);

  // ---- course home ---------------------------------------------------------------------------
  console.log('course');
  await clickScreen('.bcv-fav');
  await page.waitForSelector('.bcv-rail__item', { timeout: 10000 });
  check(page.url() === `${BASE}/courses/101`, 'favourite opens the course page');
  const tabs = await texts('.bcv-rail__item');
  check(tabs.length === 10 && tabs[0] === 'Home' && tabs[6] === 'Modules' && (await texts('.bcv-rail__title')).join(',').toLowerCase() === 'course,materials,people,campus tools', `course rail groups the tabs from the API: ${tabs.join(', ')}`);
  // dense screens fill the column up to 1180 (mockup 7 layout notes): 1400 viewport − 242 sidebar − 80 padding = 1078 here
  check(await page.$eval('.bcv-screen--ctx .bcv-head__in', (el) => Math.round(el.getBoundingClientRect().width) === 1078), `course screens fill the column (capped at 1180): ${await page.$eval('.bcv-screen--ctx .bcv-head__in', (el) => Math.round(el.getBoundingClientRect().width))}px`);
  check((await texts('.bcv-rail__ext')).join(',') === 'Resources & Policy', 'external tools are plain links under Campus tools');
  const railGlyph = await page.evaluate(() => ({ active: getComputedStyle(document.querySelector('.bcv-rail__item.is-active .bcv-rail__tile svg')), idle: getComputedStyle(document.querySelector('.bcv-rail__item:not(.is-active) .bcv-rail__tile svg')), tile: getComputedStyle(document.querySelector('.bcv-rail__item.is-active .bcv-rail__tile')).backgroundColor }));
  check(await page.$('.bcv-rail__item[data-tab="home"].is-active') && railGlyph.active.stroke === 'rgb(23, 112, 171)' && railGlyph.active.opacity === '1' && railGlyph.idle.stroke === 'rgb(23, 112, 171)' && railGlyph.idle.opacity === '0.6' && railGlyph.tile === 'rgba(0, 0, 0, 0)', `rail glyphs take the course colour (the one picked earlier), no tile, dimmed unless active: ${railGlyph.active.stroke} / ${railGlyph.idle.opacity}`);
  check(await page.$('.bcv-head .bcv-colorbtn'), 'the colour square in the course header opens the palette');
  check(!(await page.$('.bcv-head .bcv-tab')), 'no tab pills under the course title');
  await waitText('.bcv-rail__item[data-tab="announcements"] .bcv-rail__count', /^2$/);
  const gradedCount = Number((await texts('.bcv-rail__item[data-tab="grades"] .bcv-rail__count'))[0]);
  check(gradedCount >= 3, `rail counts: 2 unread announcements, ${gradedCount} grades posted this week`);
  await page.click('.bcv-rail__toggle');
  await page.waitForFunction(() => document.querySelector('.bcv-rail.is-narrow') && document.querySelector('.bcv-rail').getBoundingClientRect().width < 90, null, { timeout: 3000 }).catch(() => {}); // the width eases over .45s
  check(await page.$('.bcv-rail.is-narrow') && (await page.$eval('.bcv-rail', (el) => el.getBoundingClientRect().width < 90)) && !(await visible('.bcv-rail__title')), 'rail collapses to tiles only');
  await shot(page, '11c-course-rail-narrow');
  await page.click('.bcv-rail__toggle');
  await page.waitForFunction(() => !document.querySelector('.bcv-rail.is-narrow'), null, { timeout: 3000 });
  check((await texts('.bcv-rail__toggle'))[0] === 'Collapse', 'toggle label follows the state');
  check((await texts('.bcv-head h1'))[0] === 'F26-MATH 021 20' && (await texts('.bcv-pill--term'))[0] === 'Fall 2026', 'course header');
  // the header and rail stay put between the course's tabs: only the main column changes hands
  await page.evaluate(() => { window.__bcvRail = document.querySelector('.bcv-rail'); window.__bcvHead = document.querySelector('.bcv-head--course'); });
  await clickScreen('.bcv-rail__item[data-tab="assignments"]');
  check(page.url() === `${BASE}/courses/101/assignments` && (await page.evaluate(() => document.querySelector('.bcv-rail') === window.__bcvRail && document.querySelector('.bcv-head--course') === window.__bcvHead)) && (await page.$eval('.bcv-rail__item.is-active', (e) => e.dataset.tab)) === 'assignments' && (await page.$$('.bcv-cmain > *')).length === 1, 'a rail tab swaps only the main column: the header and rail are the same elements, the active item moved');
  await clickScreen('.bcv-rail__item[data-tab="home"]');
  check((await page.evaluate(() => document.querySelector('.bcv-rail') === window.__bcvRail)) && (await page.$eval('.bcv-rail__item.is-active', (e) => e.dataset.tab)) === 'home' && (await texts('.bcv-rail__item[data-tab="announcements"] .bcv-rail__count'))[0] === '2', 'and back to Home: still the same rail, its counts intact');
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
  // the rows fade in one after another down the column, 20ms apart, quickly (200ms each)
  const aEnter = await page.$$eval('.bcv-body .bcv-card--list .bcv-row', (els) => els.map((e) => [e.classList.contains('bcv-enter'), e.style.getPropertyValue('--bcv-delay'), e.style.getPropertyValue('--bcv-dur')]));
  check(aEnter.length >= 6 && aEnter.every(([on, , dur], i) => on && dur === '200ms' && aEnter[i][1] === `${Math.min(i * 20, 420)}ms`), `assignment rows fade in one at a time, 20ms apart: ${aEnter.slice(0, 4).map((a) => a[1]).join(',')}…`);
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
  check((await texts('.bcv-btn--primary'))[0] === 'Submit assignment', 'submit button opens our own submission flow');
  await shot(page, '14-assignment');

  // discussions
  await tab('discussions');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  const drows = await texts('.bcv-body .bcv-row');
  // 7003 was opened from the dashboard stream earlier in this run, so Canvas now reports it read
  check(drows.length === 4 && /Is there any discussion happening this week\?.*Last post.*23 replies/.test(drows[1]) && !/23 unread/.test(drows[1]) && drows.some((t) => /Discussion Quiz for this week.*1 unread/.test(t)), `discussions (read state from Canvas): ${drows[1].slice(0, 80)}`);
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
  check(page.url().endsWith('/quizzes/9011?bcv=take') && (await visible('.bcv-side')) && (await visible('.bcv-rail')) && !!(await page.$('.bcv-cmain .bcv-qz.is-embedded')) && !(await page.$eval('html', (e) => e.classList.contains('bcv-quiz'))), 'the quiz intro sits in the course column, under the header beside the rail; nothing folds away yet');
  check((await texts('.bcv-qz__h1'))[0] === 'Lec06-PreQuiz' && (await texts('.bcv-qz__bullet')).some((t) => /Time limit: 20 minutes/.test(t)) && (await texts('.bcv-qz__begin'))[0] === 'Begin attempt', 'intro card lists the quiz settings');
  check((await texts('.bcv-qz__clock'))[0] === '20 min', 'timer pill shows the limit before the attempt starts');
  await shot(page, '22c-quiz-intro');
  await page.click('.bcv-qz__begin');
  await page.waitForSelector('.bcv-qz__opt', { timeout: 10000 });
  check((await page.$$('.bcv-qz__pill')).length === 4 && (await page.$('.bcv-qz__pill:first-child.is-current')) && (await texts('.bcv-qz__qnum'))[0] === 'Question 1', 'attempt started through the API: progress pills and question 1');
  // Begin attempt folds the chrome away, smoothly: the sidebar, the course header and the rail slide to nothing
  const folded = await page.waitForFunction(() => document.documentElement.classList.contains('bcv-quiz') && getComputedStyle(document.querySelector('.bcv-side')).width === '0px' && getComputedStyle(document.querySelector('.bcv-rail')).width === '0px' && getComputedStyle(document.querySelector('.bcv-head--course')).maxHeight === '0px', null, { timeout: 5000 }).then(() => true).catch(() => false);
  check(folded && /width/.test(await page.$eval('.bcv-side', (e) => getComputedStyle(e).transitionProperty)) && !(await visible('#bcv-fab')), 'the attempt takes the page: the sidebar, header and rail fold away (a transition), the smart button steps out');
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
  // ---- quiz feedback (mockup 9): the receipt leads to the attempt's results ------------------------
  console.log('quiz feedback');
  check((await texts('.bcv-qz__donebtns .bcv-qz__big')).join('|') === 'See feedback|Back to F26-MATH 021 20|Quiz page', `receipt offers the feedback once Canvas releases results: ${(await texts('.bcv-qz__donebtns .bcv-qz__big')).join('|')}`);
  await page.click('.bcv-qz__donebtns .bcv-qz__big--primary');
  await page.waitForSelector('.bcv-fb__q', { timeout: 10000 });
  const unfolded = await page.waitForFunction(() => !document.documentElement.classList.contains('bcv-quiz') && getComputedStyle(document.querySelector('.bcv-side')).width === '242px' && getComputedStyle(document.querySelector('.bcv-rail')).width !== '0px', null, { timeout: 5000 }).then(() => true).catch(() => false);
  check(unfolded && !!(await page.$('.bcv-cmain .bcv-qz.is-embedded .bcv-fb')), 'the feedback sits back in the course column: the sidebar, header and rail return');
  const fbLine = (await texts('.bcv-fb__scoreline'))[0];
  check(/^13 \/ 17 76% 3 of 4 correct · graded /.test(fbLine) && (await page.$$('.bcv-fb__q')).length === 4 && !(await page.$('.bcv-fb__comment')), `score card from the attempt's own numbers: ${fbLine}`);
  const fbCards = await texts('.bcv-fb__q');
  check(/^Question 1 4 \/ 4 Explain What is the velocity at t = 5\? You: -3\.15 m\/s worked solution/i.test(fbCards[0]) && !/Correct:/.test(fbCards[0]) && (await page.$('.bcv-fb__q:nth-of-type(2) img.equation_image')), `a correct question: points, Explain, your answer, the instructor's solution with Canvas's equation image: ${fbCards[0]}`);
  check(/^Question 2 0 \/ 4 Why was this wrong\? .*You: -2 m Correct: -3\.15 m worked solution 17\.68 m is the position reading/i.test(fbCards[1]), `a wrong question shows the correct answer (show_correct_answers) and the incorrect-answer comment: ${fbCards[1]}`);
  check(/Question 3 4 \/ 4 .*Your instructor left no worked solution/i.test(fbCards[2]) && /Question 4 5 \/ 5 .*You: 3\.15 .*Only one root/i.test(fbCards[3]), `no solution says so; plain-text comments render too: ${fbCards[2]} | ${fbCards[3]}`);
  check((await page.$$eval('.bcv-fb > .bcv-enter', (els) => els.map((e) => e.style.getPropertyValue('--bcv-delay')))).join(',') === '0ms,45ms,90ms,135ms,180ms', 'feedback cards arrive on a 45ms stagger');
  await shot(page, '22h-quiz-feedback');
  // per-question smart topic: the panel re-titles to the question and swaps its suggestions; closing clears it
  await (await page.$$('.bcv-fb__ask'))[1].click();
  await page.waitForSelector('#bcv-smart', { timeout: 5000 });
  check((await texts('.bcv-smart__ctx'))[0] === 'Reading: Question 2 · What is the displacement between t = 0 and t = 5?', `smart panel scoped to the question: ${(await texts('.bcv-smart__ctx'))[0]}`);
  check((await texts('.bcv-smart__action-label')).join(',') === 'Walk me through this step by step,Why was my answer wrong?,Give me a similar practice problem,Find where this was covered', `question-level suggestions: ${(await texts('.bcv-smart__action-label')).join(',')}`);
  await shot(page, '22i-quiz-feedback-smart');
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('#bcv-smart'), null, { timeout: 5000 });
  await page.click('#bcv-fab');
  await page.waitForSelector('#bcv-smart', { timeout: 5000 });
  check((await texts('.bcv-smart__ctx'))[0] === 'Reading: Lec06-PreQuiz · feedback' && (await texts('.bcv-smart__action-label')).join(',') === 'What should I review?,Quiz me on the misses', `closing the panel brings the page-level suggestions back: ${(await texts('.bcv-smart__ctx'))[0]}`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('#bcv-smart'), null, { timeout: 5000 });
  await page.click('.bcv-fb__btns .bcv-qz__big:first-child');
  await page.waitForSelector('.bcv-qz__done', { timeout: 5000 });
  check((await texts('.bcv-qz__h1'))[0] === 'Attempt submitted', 'Back to receipt returns to the submitted screen');
  await page.click('.bcv-qz__donebtns .bcv-qz__big:last-child');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(page.url() === `${BASE}/courses/101/quizzes/9011` && (await texts('.bcv-grades__side, .bcv-col .bcv-row')).some((t) => /Attempt 1/.test(t)), 'back to the quiz page, which now lists the attempt');
  // attempt limits: one attempt allowed and one used → no Take button anywhere, feedback instead
  check((await texts('.bcv-detail__actions .bcv-badge'))[0] === 'No attempts left · 1 attempt allowed' && (await texts('.bcv-detail__actions .bcv-btn')).join(',') === 'See feedback,Open in Canvas' && /Attempts 1 of 1 used/.test((await texts('.bcv-detail'))[0]), `the quiz page refuses a second attempt: ${(await texts('.bcv-detail__actions'))[0]}`);
  await page.goto(`${BASE}/courses/101/quizzes/9011?bcv=take`);
  await page.waitForSelector('.bcv-qz__begin', { timeout: 10000 });
  check((await texts('.bcv-qz__begin'))[0] === 'See your feedback' && (await texts('.bcv-qz__bullet')).some((t) => /No attempts left — this quiz allows 1 attempt\./.test(t)) && (await texts('.bcv-qz__note'))[0] === '1 attempt used of 1', 'the intro cannot start another attempt either; it offers the feedback');
  await page.click('.bcv-qz__begin');
  await page.waitForSelector('.bcv-fb__q', { timeout: 10000 });
  check((await texts('.bcv-fb__btns .bcv-qz__big')).join('|') === 'Quiz overview|Back to F26-MATH 021 20', 'feedback opened from the intro links back to it');
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
  // a quiz graded before today (seeded): its attempt row opens the feedback, with the instructor's comment
  await page.goto(`${BASE}/courses/101/quizzes/9001`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check((await texts('.bcv-detail__actions .bcv-btn--primary'))[0] === 'See feedback' && (await texts('.bcv-detail__actions .bcv-badge'))[0] === 'No attempts left · 1 attempt allowed' && (await page.$eval('.bcv-col .bcv-row[href]', (a) => a.getAttribute('href'))) === '/courses/101/quizzes/9001?bcv=feedback&sub=qs1', 'a used-up quiz leads to its feedback from the button and the attempt row');
  await page.click('.bcv-col .bcv-row[href]');
  await page.waitForSelector('.bcv-fb__q', { timeout: 10000 });
  const fbLine2 = (await texts('.bcv-fb__scoreline'))[0];
  const fbComments = await texts('.bcv-fb__ctext');
  check(/^13 \/ 16 81% 3 of 4 correct · graded /.test(fbLine2) && fbComments.length === 1 && /^Nice work on the derivative questions/.test(fbComments[0]) && (await texts('.bcv-fb__avatar'))[0] === 'YL' && (await texts('.bcv-fb__btns .bcv-qz__big')).join('|') === 'Back to F26-MATH 021 20', `feedback from the quiz page: the instructor's comment (never your own reply): ${fbLine2} · ${fbComments.join(' | ')}`);
  check(/Question 2 0 \/ 4 .*You: 17\.68 m Correct: -3\.15 m/i.test((await texts('.bcv-fb__q'))[1]), `the seeded wrong answer with the correct one beside it: ${(await texts('.bcv-fb__q'))[1]}`);
  await shot(page, '22j-quiz-feedback-seeded');

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
  check(actions.join(',') === 'Summarize this assignment,Make a checklist,Check what I am handing in', `suggested actions (the page's own, plus the hand-in block's): ${actions.join(', ')}`);
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
  check((await page.$('.bcv-reader-btn')) && !(await visible('.bcv-reader-btn')), 'Immersive Reader button is left out where there is nothing to read (Grades)');
  await shot(page, '26-dark-grades');
  await page.goto(`${BASE}/courses/101`);
  await page.waitForSelector('.bcv-front', { timeout: 10000 });
  check(await visible('.bcv-reader-btn'), 'Immersive Reader button is there on the course front page');
  await shot(page, '27-dark-course-home');
  // punch-through pages: the hole is darkened by a filter unless "View in light mode" is on
  await page.goto(`${BASE}/courses/101/external_tools/9`);
  await page.waitForSelector('html.bcv-punch #content', { timeout: 10000 });
  await page.waitForSelector('.bcv-native__light', { timeout: 5000 });
  check((await page.$eval('#content', (el) => getComputedStyle(el).filter)) !== 'none' && (await texts('.bcv-native__light'))[0] === 'View in light mode', 'dark appearance darkens the Canvas-drawn page and offers light mode at the top right of the hole');
  await page.click('.bcv-native__light');
  await page.waitForFunction(() => document.documentElement.classList.contains('bcv-punch-light'), null, { timeout: 5000 });
  check((await page.$eval('#content', (el) => getComputedStyle(el).filter)) === 'none' && (await texts('.bcv-native__light'))[0] === 'Back to dark', '"View in light mode" shows the hole as Canvas drew it');
  await shot(page, '27b-dark-punch-light');
  await page.reload();
  await page.waitForSelector('html.bcv-punch.bcv-punch-light #content', { timeout: 10000 });
  check((await texts('.bcv-native__light'))[0] === 'Back to dark', 'the light-mode choice is remembered for the site');
  await page.click('.bcv-native__light');
  await page.waitForFunction(() => !document.documentElement.classList.contains('bcv-punch-light'), null, { timeout: 5000 });
  check((await page.$eval('#content', (el) => getComputedStyle(el).filter)) !== 'none', '"Back to dark" darkens it again');
  await page.goto(`${BASE}/courses/101`);
  await page.waitForSelector('.bcv-front', { timeout: 10000 });
  await page.click('#bcv-theme-btn');
  await page.waitForFunction(() => document.documentElement.getAttribute('data-bcv-theme') === 'light', null, { timeout: 5000 });

  // ---- loading in the background: the next press is ready before it happens -------------------
  console.log('background loading');
  {
    // every API request the page makes, in the order issued, and how many were out at once: the
    // route answers each one itself (fetch, then fulfil), so what is in flight is counted exactly
    const api = [];
    let open = 0, peak = 0;
    // the announcements feed is the slow call: held back, the Dashboard must paint without it
    const feedRe = /\/api\/v1\/announcements\?/;
    let holdFeed = true; // every feed request (the mock refuses one course, so it is asked again per course) waits while this holds
    const counter = '**/api/v1/**';
    await page.route(counter, async (route) => {
      const url = route.request().url();
      api.push(url.replace(BASE, '').replace(/\?.*$/, ''));
      if (holdFeed && feedRe.test(url)) await new Promise((r) => setTimeout(r, 2500));
      open++; peak = Math.max(peak, open);
      try {
        const response = await route.fetch();
        open--;
        await route.fulfill({ response });
      } catch {
        open--;
        await route.abort().catch(() => {});
      }
    });
    await page.goto(`${BASE}/`);
    await page.waitForSelector('.bcv-stat', { timeout: 10000 });
    const early = await page.evaluate(() => [...document.querySelectorAll('.bcv-stat__value')].map((e) => e.textContent.trim()));
    check(early.length === 6 && /^\d+$/.test(early[0]) && /^\d+$/.test(early[1]) && early[2] === '…', `the Dashboard paints without waiting for the announcements feed, its counter still to come: ${early.join(' | ')}`);
    holdFeed = false;
    await waitText('.bcv-stats > :nth-child(3) .bcv-stat__value', /^\d+$/);
    check(true, 'and that counter fills in when the feed lands');
    await page.waitForTimeout(2500); // the page went idle: the other screens' first requests are made now
    // the Dashboard itself never asks for the inbox or for assignment groups: those are the Inbox's
    // and the Grades' first requests, made in the background (the timing is not pinned — the
    // warming starts the moment the screen settles, before the harness can look)
    const warmed = api.filter((u) => /\/api\/v1\/conversations$|\/api\/v1\/courses\/\d+\/assignment_groups$/.test(u));
    const has = (re) => warmed.some((u) => re.test(u));
    check(has(/\/api\/v1\/conversations$/) && has(/\/assignment_groups$/), `once it settled, the Inbox and the Grades asked for their data without a press (${warmed.length} such requests, ${api.length} in all)`);
    check(peak <= 10 && peak >= 5, `never more than ten requests in flight, however many were asked for at once (peak ${peak})`);
    const before = api.length;
    await nav('groups');
    await nav('inbox');
    await nav('gpa');
    await page.waitForSelector('.bcv-gpa__value', { timeout: 10000 });
    check(api.length === before, `Groups, Inbox and Grades then open from the memo, with no request at all (${api.length - before} made)`);
    // inside a course, hovering a tab's rail row starts its data; the press then lands from the memo
    await page.click('.bcv-fav');
    await page.waitForSelector('.bcv-rail [data-tab="quizzes"]', { timeout: 10000 });
    await page.waitForTimeout(2500); // every other tab warms on idle; quizzes among them
    const beforeTab = api.length;
    await tab('quizzes');
    check(api.length === beforeTab && (await page.$$('.bcv-body .bcv-row')).length > 0, `a course tab warmed on idle opens with no request (${api.length - beforeTab} made)`);
    await page.unroute(counter);
  }

  // ---- motion (mockup 8): entrances by keyframes, a loading bar + skeletons, reduced motion ----
  console.log('motion');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 10000 });
  await page.waitForFunction(() => document.documentElement.classList.contains('bcv-settled') && !document.querySelector('.bcv-load'), null, { timeout: 5000 });
  const motion = await page.evaluate(() => ({
    screen: getComputedStyle(document.querySelector('.bcv-main > .bcv-screen')).animationName,
    noBar: !document.getElementById('bcv-progress') && !document.querySelector('.bcv-load'),
  }));
  check(motion.screen === 'bcv-fade-up' && motion.noBar, `screens rise in by keyframe; no page-top bar, and no row wash once the screen is drawn: ${JSON.stringify(motion)}`);
  // mockup 14: the pressed control is the progress bar. A sidebar row that starts a load fills left to
  // right with a flat wash in its own icon colour, under its label; a second press is a no-op.
  const slowCal = /\/calendar(\?.*)?$/; // the next page's own document is held back, so the pressed row's wash can be watched
  // It holds back one document and then steps aside on its own. (Unrouting instead would race the
  // request it is already holding, and that document arrives empty: no page, no extension, no screen.)
  let holdCal = true;
  await page.route(slowCal, async (route) => {
    if (route.request().resourceType() !== 'document' || !holdCal) { await route.continue().catch(() => {}); return; }
    holdCal = false;
    await new Promise((r) => setTimeout(r, 900));
    await route.continue().catch(() => {});
  });
  const wash = await page.evaluate(() => { // the press and the reading in one task: the wash is painted synchronously by the press
    const row = document.querySelector('.bcv-nav__item[data-nav="calendar"]');
    row.click();
    const fill = row.querySelector('.bcv-load');
    if (!fill) return { fill: false };
    const cs = getComputedStyle(fill);
    row.click(); // the row already loading: nothing queued, nothing restarted
    return { anim: cs.animationName, origin: cs.transformOrigin, bg: cs.backgroundColor, loading: row.classList.contains('is-loading'), under: getComputedStyle(row.querySelector('.bcv-nav__ic')).position, clipped: getComputedStyle(row).overflow, others: document.querySelectorAll('.bcv-load').length, stillOne: row.querySelectorAll('.bcv-load').length };
  }).catch((e) => ({ error: e.message }));
  check(wash && wash.anim === 'bcv-load' && /^0px/.test(wash.origin) && wash.bg === 'rgba(88, 86, 214, 0.2)' && wash.loading && wash.under === 'relative' && wash.clipped === 'hidden' && wash.others === 1 && wash.stillOne === 1, `Calendar fills its own row with a 20% wash of its indigo, from the left, under the label; no other row lights and a second press changes nothing: ${JSON.stringify(wash)}`);
  const washGone = await page.waitForFunction(() => location.pathname === '/calendar' && document.documentElement.classList.contains('bcv-settled') && !document.querySelector('.bcv-load'), null, { timeout: 15000 }).then(() => true).catch(() => false);
  check(washGone, `the wash leaves when the next page has drawn its screen${washGone ? '' : `: ${JSON.stringify(await page.evaluate(() => ({ url: location.href, settled: document.documentElement.classList.contains('bcv-settled'), loads: document.querySelectorAll('.bcv-load').length, app: !!document.getElementById('bcv-app') })).catch((e) => e.message))}`}`);
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 10000 });
  // the cards on this screen arrive together rather than one after another: one fade-up, no delay
  const stagger = await page.evaluate(() => [...document.querySelectorAll('.bcv-stat.bcv-enter')].map((e) => `${getComputedStyle(e).animationName}@${getComputedStyle(e).animationDelay}`));
  check(stagger.length === 6 && stagger.every((a) => a === 'bcv-fade-up@0s'), `the six stat cards land on the same beat: ${stagger.join(',')}`);
  // mockup 11: data animates in — counters roll (and land exactly), workload bars wipe from the left, rows float in
  const workAnim = await page.evaluate(() => ({
    bars: [...document.querySelectorAll('.bcv-work__fill--grow')].map((e) => `${getComputedStyle(e).animationName}@${getComputedStyle(e).animationDelay}`),
    rows: [...document.querySelectorAll('.bcv-work__row--in')].map((e) => `${getComputedStyle(e).animationName}@${getComputedStyle(e).animationDelay}`),
    origin: getComputedStyle(document.querySelector('.bcv-work__fill--grow')).transformOrigin,
  }));
  check(workAnim.bars.slice(0, 2).join(',') === 'bcv-grow@0.14s,bcv-grow@0.23s' && workAnim.rows.slice(0, 2).join(',') === 'bcv-fade-up@0.09s,bcv-fade-up@0.16s' && /^0px/.test(workAnim.origin), `workload bars wipe from the left 90ms apart, rows float in 70ms apart: ${JSON.stringify(workAnim)}`);
  const dueNow = (await texts('.bcv-stat__value'))[0]; // the real count at this point of the run (items were ticked earlier)
  await page.gotoRaw(`${BASE}/`); // raw: the roll itself is what is being checked
  await page.waitForSelector('.bcv-stat__value[data-rolling]', { timeout: 10000 });
  const midRoll = await page.evaluate(() => [...document.querySelectorAll('.bcv-stat__value')].map((e) => e.textContent));
  await page.waitForFunction(() => !document.querySelector('[data-rolling]'), null, { timeout: 5000 });
  const landed = await texts('.bcv-stat__value');
  // (a card whose own request is still out shows "…" rather than a number, and is not a counter yet)
  check(midRoll.every((t) => /^\d+$/.test(t) || t === '…') && midRoll.some((t) => /^\d+$/.test(t)) && landed[0] === dueNow && (await page.evaluate(() => !document.querySelector('.bcv-stat__value[data-rolling]'))), `counters run through plausible digits and land on the real count: ${midRoll.join(',')} → ${landed.join(',')} (Due today is ${dueNow})`);
  // a view switch after entry redraws the counters without rolling them again
  await page.click('.bcv-seg__btn:nth-child(2)');
  await page.waitForSelector('.bcv-day', { timeout: 10000 });
  check(!(await page.$('[data-rolling]')) && (await texts('.bcv-stat__value'))[0] === dueNow && !(await page.$('.bcv-work__fill--grow')), 'a number already on screen never rolls again (entry only)');
  // cards lift under the pointer once they have landed (the entrance fill is backwards, so the hover transform takes)
  await page.waitForTimeout(700);
  await page.hover('.bcv-stat');
  await page.waitForTimeout(300);
  const lift = await page.evaluate(() => ({ t: getComputedStyle(document.querySelector('.bcv-stat')).transform, tr: getComputedStyle(document.querySelector('.bcv-stat')).transitionProperty }));
  check(lift.t === 'matrix(1, 0, 0, 1, 0, -2)' && /transform/.test(lift.tr), `a stat card lifts 2px on hover: ${JSON.stringify(lift)}`);
  await page.mouse.move(5, 5);
  await nav('gpa');
  await page.waitForSelector('.bcv-gpa__card', { timeout: 10000 });
  const gpaStagger = await page.evaluate(() => ({
    top: [...document.querySelectorAll('.bcv-gpa__top > .bcv-enter')].map((e) => e.style.getPropertyValue('--bcv-delay')),
    cards: [...document.querySelectorAll('.bcv-gpa__card.bcv-enter')].map((e) => e.style.getPropertyValue('--bcv-delay')),
    rings: [...document.querySelectorAll('.bcv-gpa__card .bcv-ring--fill')].map((e) => `${getComputedStyle(e).animationName}@${getComputedStyle(e).animationDelay}`),
    value: document.querySelector('.bcv-gpa__value').textContent,
  }));
  check(gpaStagger.top.join(',') === '40ms,110ms' && gpaStagger.cards[0] === '0ms' && gpaStagger.cards[1] === '55ms' && Math.max(...gpaStagger.cards.map(parseFloat)) <= 420, `Grades: hero, then trend, then cards on 55ms, capped at 420ms: ${JSON.stringify(gpaStagger)}`);
  check(gpaStagger.rings.length === 4 && gpaStagger.rings.slice(0, 3).join(',') === 'bcv-ring-fill@0s,bcv-ring-fill@0.07s,bcv-ring-fill@0.14s' && /^3\.4[23]$/.test(gpaStagger.value), `course rings sweep to their score 70ms apart and the GPA has landed: ${gpaStagger.rings.join(',')} · ${gpaStagger.value}`);
  await page.hover('.bcv-gpa__card .bcv-gpa__ringbox');
  await waitText('.bcv-gpa__card', /By group/);
  const cats = await page.evaluate(() => [...document.querySelectorAll('.bcv-gpa__card.is-hover .bcv-ring--fill-cat')].map((e) => `${getComputedStyle(e).animationName}@${getComputedStyle(e).animationDuration}@${getComputedStyle(e).animationDelay}`));
  check(cats.length >= 1 && cats[0] === 'bcv-ring-fill@0.55s@0s' && (cats.length < 2 || cats[1] === 'bcv-ring-fill@0.55s@0.09s'), `group rings fill in as they mount, 90ms apart: ${cats.join(',')}`);
  await page.mouse.move(5, 5);
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 10000 });
  await page.click('.bcv-stat');
  await page.waitForSelector('.bcv-sheet', { timeout: 5000 });
  const sheetAnim = await page.evaluate(() => [getComputedStyle(document.querySelector('.bcv-sheet-ov')).animationName, getComputedStyle(document.querySelector('.bcv-sheet')).animationName, getComputedStyle(document.querySelector('.bcv-sheet')).animationDuration, document.querySelector('.bcv-sheet').style.transformOrigin]);
  check(sheetAnim[0] === 'bcv-scrim' && sheetAnim[1] === 'bcv-morph' && sheetAnim[2] === '0.34s' && /^-?\d+px -?\d+px$/.test(sheetAnim[3]), `a sheet grows out of the counter that opened it, over a scrim that blurs in: ${sheetAnim.join(' / ')}`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet-ov'), null, { timeout: 5000 });
  // a slow response: the bar keeps sweeping and skeleton rows hold the place; both leave when the data lands
  const slow = /\/api\/v1\/courses\/104\/assignments\/4002(\?|$)/;
  await page.route(slow, async (route) => { await new Promise((r) => setTimeout(r, 900)); await route.continue().catch(() => {}); }); // a request still waiting when the route is removed just goes through
  await page.gotoRaw(`${BASE}/courses/104/assignments/4002?bcv=submit`); // raw: the skeleton is what is being checked
  await page.waitForSelector('.bcv-skel', { timeout: 10000 });
  const skel = await page.evaluate(() => {
    const s = document.querySelector('.bcv-skel');
    const fav = document.querySelector('.bcv-fav.is-loading .bcv-load');
    return { aria: s.getAttribute('aria-hidden'), rows: s.querySelectorAll('.bcv-skel__row').length, shimmer: getComputedStyle(s.querySelector('.bcv-skel__b')).animationName, delay: getComputedStyle(s).animationDelay, favWash: fav ? getComputedStyle(fav).backgroundColor : null, favRow: document.querySelector('.bcv-fav.is-loading')?.textContent.trim() };
  });
  check(skel.aria === 'true' && skel.rows === 6 && skel.shimmer === 'bcv-shimmer' && skel.delay === '0.15s' && /^rgba\(\d+, \d+, \d+, 0\.2\)$/.test(skel.favWash) && skel.favRow === 'F26-SPRK 010 103', `while a response is slow the course's own sidebar row fills with its colour and skeleton rows shimmer, hidden from screen readers: ${JSON.stringify(skel)}`);
  await page.unroute(slow);
  await page.waitForSelector('.bcv-sb__foot', { timeout: 15000 });
  check(!(await page.$('.bcv-skel')) && !(await page.$('.bcv-load')), 'the skeleton and the wash leave when the content lands');
  // the course rail loads the same way, in the course colour, with its own key: only the pressed row.
  // Every tab's data is asked for in the background once the course has landed, so the hold is in
  // place before the page: the discussions request — whether the background's or the press's own —
  // is still out when the row is pressed, and the row fills until it answers.
  const slowDisc = /\/api\/v1\/courses\/101\/discussion_topics(\?|$)/; // Discussions is not read by Home, so the tab really fetches
  await page.route(slowDisc, async (route) => { await new Promise((r) => setTimeout(r, 900)); await route.continue().catch(() => {}); });
  await page.goto(`${BASE}/courses/101`);
  await page.waitForSelector('.bcv-rail__item', { timeout: 10000 });
  await page.waitForFunction(() => document.documentElement.classList.contains('bcv-settled') && !document.querySelector('.bcv-load'), null, { timeout: 10000 });
  await page.evaluate(() => document.querySelector('.bcv-rail__item[data-tab="discussions"]').click());
  await page.waitForSelector('.bcv-rail__item[data-tab="discussions"] .bcv-load--rail', { timeout: 3000 });
  const railWash = await page.evaluate(() => ({ bg: getComputedStyle(document.querySelector('.bcv-rail__item[data-tab="discussions"] .bcv-load')).backgroundColor, lit: [...document.querySelectorAll('.bcv-load')].length, sidebar: !!document.querySelector('#bcv-side .bcv-load'), railColor: getComputedStyle(document.querySelector('.bcv-screen--ctx')).getPropertyValue('--bcv-rail-color').trim().toLowerCase() }));
  check(railWash.lit === 1 && !railWash.sidebar && railWash.railColor === '#1770ab' && railWash.bg === 'rgba(23, 112, 171, 0.2)', `a rail row fills with the course colour at 20%, and the sidebar stays dark (its own key): ${JSON.stringify(railWash)}`);
  await page.unroute(slowDisc);
  await page.waitForFunction(() => location.pathname.endsWith('/discussion_topics') && document.documentElement.classList.contains('bcv-settled') && !document.querySelector('.bcv-load'), null, { timeout: 10000 });
  check(true, 'the rail wash leaves when the column lands');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 10000 });
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await page.reload();
  await page.waitForSelector('.bcv-stat', { timeout: 10000 });
  const reduced = await page.evaluate(() => {
    const probe = document.createElement('span'); probe.className = 'bcv-load'; document.querySelector('.bcv-nav__item').append(probe); // a wash under reduced motion: a still, partial fill
    const cs = getComputedStyle(probe);
    const out = [getComputedStyle(document.querySelector('.bcv-main > .bcv-screen')).animationName, `${cs.animationName}/${cs.transform}`, getComputedStyle(document.querySelector('.bcv-stat.bcv-enter')).animationName, getComputedStyle(document.querySelector('.bcv-work__fill--grow')).animationName, document.querySelector('.bcv-stat__value').textContent];
    probe.remove();
    return out;
  });
  check(reduced[0] === 'none' && reduced[1] === 'none/matrix(0.6, 0, 0, 1, 0, 0)' && reduced[2] === 'none' && reduced[3] === 'none' && reduced[4] === dueNow, `reduced motion drops the entrances (and the stagger, the bar wipe, the counter roll); the row wash holds still part way: ${reduced.join(' / ')}`);
  await page.emulateMedia({ reducedMotion: 'no-preference' });

  // ---- the look switched off from settings (popup / options) -------------------------------------------
  console.log('look off from settings');
  await page.goto(`${BASE}/courses/101/external_tools/9`);
  await page.waitForSelector('html.bcv-punch #content', { timeout: 10000 });
  await page.click('.bcv-native__stock');
  await page.waitForFunction(() => !document.documentElement.classList.contains('bcv-on'), null, { timeout: 5000 });
  check(await visible('#application') && !(await visible('#bcv-app')), '"Open in stock Canvas" turns the look off: stock Canvas is back');
  check(!(await page.$('html.bcv-punch')) && (await visible('#header')) && (await page.$eval('#content', (el) => el.getBoundingClientRect().left < 200)), 'turning the look off ends the punch-through: Canvas lays its page out itself again');
  check(!(await page.$('#bcv-fab')) && !(await page.$('#bcv-skin')), 'smart button hidden, nothing of ours left on the page');
  await shot(page, '28-skin-off');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#application', { timeout: 10000 });
  await page.waitForTimeout(400);
  check(!(await page.$('#bcv-app')) || !(await visible('#bcv-app')), 'the look stays off on the next page load');
  await setSettings({ appearance: { skin: true } });
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  check(await visible('#bcv-app'), 'switching the look back on from settings applies in place');
  await nav('courses');
  await page.waitForSelector('.bcv-ccard__hero--term', { timeout: 10000 });
  await setSettings({ appearance: { skin: false } });
  await page.waitForFunction(() => !document.documentElement.classList.contains('bcv-on'), null, { timeout: 5000 });
  check(page.url() === `${BASE}/courses` && (await visible('#application')) && (await page.title()).includes('courses'), 'look off after navigating shows the same page in stock Canvas (it was underneath all along)');
  await setSettings({ appearance: { skin: true } });
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });

  // ---- guided setup + the tour --------------------------------------------------------------------------
  console.log('guided setup');
  const apiGet = (p) => fetch(`${BASE}${p}`).then((r) => r.text()).then((t) => JSON.parse(t.replace(/^while\(1\);/, '')));
  const prefsOf = async () => { const all = await sw.evaluate(() => self.BCV.api.storage.local.get(null)); return all['prefs:localhost:8787'] || {}; };
  const su = (sel) => `#bcv-setup ${sel}`; // the setup's card lives in a shadow root under #bcv-setup
  const sStep = () => page.$eval(su('#stepLabel'), (e) => e.textContent.trim()).catch(() => '');
  const sNext = async (waitFor) => { await page.click(su('#next')); await page.waitForSelector(su(waitFor), { timeout: 15000 }); await page.waitForTimeout(450); };
  // a fresh student: the Grades checks above left a goal and a record behind
  await sw.evaluate(async () => { const k = 'prefs:localhost:8787'; const all = await self.BCV.api.storage.local.get(k); const p = all[k] || {}; for (const key of ['gpaGoal', 'gpaTracking', 'gradeTargets', 'setupDone', 'tour']) delete p[key]; await self.BCV.api.storage.local.set({ [k]: p }); await self.BCV.api.storage.local.remove('setup:done'); });
  await page.goto(`${BASE}/?bcv=setup`);
  await page.waitForSelector(su('.row'), { timeout: 20000 });
  await page.waitForTimeout(500);
  check(page.url() === `${BASE}/` && (await page.$('.bcv-stat')) !== null && (await page.$$(su('.blob'))).length === 4 && (await sStep()) === '1 of 4' && (await page.$$(su('.progress span'))).length === 4 && (await page.$eval('html', (e) => getComputedStyle(e).overflow)) === 'hidden', 'the setup opens as a glass card over the dashboard: the address cleaned, four steps, the page held still');
  await shot(page, '32-setup-over-page');
  const scanned = await texts(su('.row__code'));
  // nothing is ticked to begin with, whatever Canvas already has starred, and Continue is dead until
  // something is: the list picked here is the one every screen then follows
  check((await texts(su('.h1')))[0] === 'Which are you in?' && scanned.length >= 8 && (await page.$$(su('.row.is-on'))).length === 0 && (await texts(su('.listhead span')))[0] === '0 selected' && (await page.$eval(su('#next'), (b) => b.disabled)), `step 1 read the enrolments with none preselected: ${scanned.length} active courses, 0 checked, Continue off`);
  const rowCodes = await page.$$eval(su('.row[data-course]'), (els) => els.map((e) => [e.dataset.course, e.querySelector('.row__code').textContent.trim()]));
  const starredBefore = (await apiGet('/api/v1/courses?per_page=100')).filter((c) => c.is_favorite).map((c) => String(c.id));
  // Pick five: one Canvas had not starred and one starred course deliberately left out, so the write
  // is exercised in both directions whatever the checks above left behind.
  const starredRows = rowCodes.filter(([id]) => starredBefore.includes(id));
  const freshRows = rowCodes.filter(([id]) => !starredBefore.includes(id));
  check(starredRows.length >= 1 && freshRows.length >= 1 && rowCodes.length >= 6, `the mock offers both starred and unstarred courses to pick between: ${starredRows.length} + ${freshRows.length}`);
  const dropped = starredRows[0];
  const picks = [freshRows[0], ...starredRows.slice(1), ...freshRows.slice(1)].slice(0, 5);
  const onCode = freshRows[0][1];
  const offCode = dropped[1];
  for (const [id] of picks) await page.click(su(`.row[data-course="${id}"]`));
  check((await texts(su('.listhead span')))[0] === '5 selected' && (await page.$$(su('.row.is-on'))).length === 5 && !(await page.$eval(su('#next'), (b) => b.disabled)), 'rows toggle with the count, and Continue comes alive once one is picked');
  await page.click(su(`.row[data-course="${picks[0][0]}"]`));
  check((await texts(su('.listhead span')))[0] === '4 selected' && (await page.$$(su('.row.is-on'))).length === 4, 'and a second press unticks it');
  await page.click(su(`.row[data-course="${picks[0][0]}"]`));
  // a nickname typed on a row is saved with the rest (Canvas's own nickname)
  const nickRow = page.locator(su('.row.is-on')).last();
  const nickId = await nickRow.getAttribute('data-course');
  await nickRow.locator('.row__nick').fill('Setup nick');
  check((await page.$$(su('.row.is-on'))).length === 5 && (await page.$eval(su('.row__nick'), (e) => e.placeholder)) === 'Nickname', 'every row has a Nickname field; typing in one does not toggle the row');
  await shot(page, '32c-setup-courses');
  await sNext('#track');
  const firstTargets = await page.$$eval(su('.target .seg button.is-on'), (bs) => bs.map((b) => b.textContent));
  check((await sStep()) === '2 of 4' && (await page.$eval(su('#track'), (e) => e.classList.contains('is-on'))) && (await texts(su('#goal')))[0] === '4.00' && (await page.$$(su('.target'))).length === 5 && firstTargets.join(',') === 'A+,A+,A+,A+,A+' && (await page.$$eval(su('.target:first-child .seg button'), (bs) => bs.map((b) => b.textContent))).join(' ') === 'C B B+ A- A A+', `step 2: tracking on, a 4.00 goal, every course aiming at A+, letters low to high: ${firstTargets.join(',')}`);
  await page.click(su('.stepper button:last-child')); // already at the top of the scale: it stays there
  check((await texts(su('#goal')))[0] === '4.00', `the goal does not climb past 4.00: ${(await texts(su('#goal')))[0]}`);
  await page.click(su('.stepper button:first-child'));
  await page.click(su('.stepper button:first-child'));
  await page.click(su('.target:first-child .seg button:nth-child(3)'));
  check((await texts(su('#goal')))[0] === '3.90' && (await page.$eval(su('.target:first-child .seg button.is-on'), (b) => b.textContent)) === 'B+', 'the goal stepper and a target pick');
  await shot(page, '32d-setup-grades');
  await sNext('.prov');
  const sProvs = await texts(su('.prov'));
  const purpose = (await texts(su('#purpose')))[0];
  check((await sStep()) === '3 of 4' && sProvs.length === 3 && /Gemini\s*Coming soon/.test(sProvs[2]) && (await page.$eval(su('.prov.is-soon'), (b) => b.getAttribute('aria-disabled'))) === 'true' && /console\.anthropic\.com/.test((await texts(su('.keystep')))[0]) && !(await page.$eval(su('#notNow'), (b) => b.hidden)), `step 3 offers Claude, ChatGPT and Gemini (coming soon) with the key steps: ${sProvs.join(' | ')}`);
  // the only two ways off this step: a key that checks out, or Not now. Continue is dead while the
  // field is empty, so the panel is never left half-set-up by pressing the blue button to get past.
  check((await page.$eval(su('#next'), (b) => b.disabled)) && !(await page.$eval(su('#notNow'), (b) => b.hidden)), 'with no key Continue is off and Not now is the way on');
  await page.$eval(su('#next'), (el) => el.click()); // dispatched straight at it: page.click() would wait for it to become enabled
  await page.waitForTimeout(300);
  check((await sStep()) === '3 of 4', `and pressing it anyway does nothing: still ${await sStep()}`);
  check(/^Smart Panel is intended to be a smart assistant that helps with learning\. It is not intended to help complete assignments, cheat on quizzes/.test(purpose) && /^rgb\(2(29|55), (55|105), (43|97)\)$/.test(await page.$eval(su('#purpose'), (e) => getComputedStyle(e).color)), `the purpose notice, in red: ${await page.$eval(su('#purpose'), (e) => getComputedStyle(e).color)}`);
  await page.click(su('.prov[data-provider="openai"]'));
  check(/platform\.openai\.com/.test((await texts(su('.keystep')))[0]), 'ChatGPT swaps the key steps');
  await page.fill(su('#key'), 'sk-test-setup');
  check((await page.$eval(su('#notNow'), (b) => b.hidden)) && !(await page.$eval(su('#next'), (b) => b.disabled)), 'typing a key hides Not now and wakes Continue');
  await page.click(su('#next'));
  await page.waitForFunction((s) => /rejected|Could not|works|Unauthorized|invalid|checked/i.test(document.querySelector(s).shadowRoot.querySelector('#keyResult').textContent), '#bcv-setup', { timeout: 20000 });
  check((await page.$eval(su('#keyResult'), (e) => e.classList.contains('is-err'))) && (await sStep()) === '3 of 4' && !(await page.$eval(su('#next'), (b) => b.disabled)), `a key that does not validate stays on the step, with Continue still pressable to try again: ${await page.$eval(su('#keyResult'), (e) => e.textContent)}`);
  await page.fill(su('#key'), '');
  check((await page.$eval(su('#next'), (b) => b.disabled)) && !(await page.$eval(su('#notNow'), (b) => b.hidden)), 'clearing the field puts Continue back to sleep and Not now back');
  await shot(page, '32e-setup-smart');
  await page.click(su('#notNow'));
  // step 4: where the courses chosen in step 1 should sit
  await page.waitForSelector(su('.row[data-value]'), { timeout: 10000 });
  await page.waitForTimeout(450);
  const sideOpts = await texts(su('.row[data-value] .row__code'));
  check((await sStep()) === '4 of 4' && sideOpts.join(' | ') === 'Always on the sidebar | When I hover on Courses' && (await page.$eval(su('.row[data-value="always"]'), (e) => e.classList.contains('is-on'))) && (await page.$eval(su('#next'), (e) => e.textContent)) === 'Finish', `step 4 asks where the courses live, on the sidebar by default: ${sideOpts.join(' | ')}`);
  await shot(page, '32f-setup-sidebar');
  const pickSide = async (v) => {
    await page.click(su(`.row[data-value="${v}"]`));
    await page.waitForFunction((val) => document.querySelector('#bcv-setup').shadowRoot.querySelector(`.row[data-value="${val}"]`).classList.contains('is-on'), v, { timeout: 5000 });
    return sw.evaluate(async () => (await self.BCV.settings.get()).appearance.sideCourses);
  };
  check((await pickSide('hover')) === 'hover', 'picking one writes it as it is chosen, no Finish needed');
  check((await pickSide('always')) === 'always' && (await page.$$(su('.row[data-value].is-on'))).length === 1, 'and changing the pick moves the tick, one at a time'); // back to the default: the checks below read the sidebar list
  await page.click(su('#next'));
  await page.waitForSelector(su('.done'), { timeout: 10000 });
  const doneCard = await page.evaluate(() => { const r = document.querySelector('#bcv-setup')?.shadowRoot; return r ? [r.querySelector('#stepLabel')?.textContent.trim(), r.querySelector('.done .h1')?.textContent.trim()] : null; });
  check(doneCard && doneCard[0] === 'Done' && doneCard[1] === 'All set', `Finish on the last step: a moment of All set (${JSON.stringify(doneCard)})`);
  await page.waitForSelector('.bcv-tour__card', { timeout: 20000 });
  check(page.url() === `${BASE}/` && (await page.$('#bcv-setup')) === null && !(await page.$('html.bcv-setup-open')) && (await page.$eval('.bcv-tour__title', (e) => e.textContent.trim())) === 'Your day at a glance', 'then the card closes and the tour starts on the same page');
  const favAfter = (await apiGet('/api/v1/courses?per_page=100')).filter((c) => c.is_favorite).map((c) => c.course_code || c.name);
  check(!favAfter.includes(offCode) && favAfter.includes(onCode) && favAfter.length === 5, `the chosen courses became the Canvas favourites: −${offCode} +${onCode}`);
  const nickedInSetup = (await apiGet('/api/v1/courses?per_page=100')).find((c) => String(c.id) === nickId);
  check(nickedInSetup.name === 'Setup nick' && !!nickedInSetup.original_name && (await texts('.bcv-fav')).includes('Setup nick'), `the nickname typed in setup reached Canvas and the sidebar: ${nickedInSetup.original_name} → ${nickedInSetup.name}`);
  await fetch(`${BASE}/api/v1/users/self/course_nicknames/${nickId}`, { method: 'DELETE', headers: { 'x-csrf-token': 'mock+csrf/token=' } }); // back to the real name for what follows
  check((await texts('.bcv-fav')).length === 5, 'the sidebar follows the new favourites');
  const savedPrefs = await prefsOf();
  check(savedPrefs.gpaGoal === 3.9 && savedPrefs.gpaTracking?.since && savedPrefs.gpaTracking.priorGpa === null && Object.values(savedPrefs.gradeTargets || {}).includes('B+') && savedPrefs.setupDone === true && (await sw.evaluate(async () => (await self.BCV.api.storage.local.get('setup:done'))['setup:done'])) === true, `the grade choices landed where the Grades page reads them, and the done flags are set: ${JSON.stringify({ goal: savedPrefs.gpaGoal, tracking: savedPrefs.gpaTracking, targets: savedPrefs.gradeTargets })}`);
  const tourTitle = () => page.$eval('.bcv-tour__title', (e) => e.textContent.trim()).catch(() => '');
  // headless Chromium only advances CSS animations when it paints a frame: let the screen's entrance finish first
  // (a poll that paints a frame each time, so the entrance and the ring's own transition can play out)
  const ringOver = (sel) => eventually(() => page.evaluate(async (s) => {
    await new Promise((r) => requestAnimationFrame(() => requestAnimationFrame(r)));
    const ring = document.querySelector('.bcv-tour__ring');
    const el = document.querySelector(s);
    if (!ring || !el) return false;
    const r = ring.getBoundingClientRect();
    const t = el.getBoundingClientRect();
    return r.width > 0 && r.left <= t.left + 1 && r.top <= t.top + 1 && r.right >= t.right - 1 && r.bottom >= t.bottom - 1;
  }, sel).catch(() => false), 4000);
  const rects = () => page.evaluate(() => { const rr = (el) => { if (!el) return null; const r = el.getBoundingClientRect(); return [r.left, r.top, r.right, r.bottom].map(Math.round); }; return JSON.stringify({ ring: rr(document.querySelector('.bcv-tour__ring')), stats: rr(document.querySelector('.bcv-stats')), stat: rr(document.querySelector('.bcv-stat')), n: document.querySelectorAll('.bcv-stat').length, anim: document.getAnimations().length, tf: getComputedStyle(document.querySelector('.bcv-stat')).transform }); });
  await page.waitForTimeout(700); // the dashboard's entrance, then the ring settles on the counters
  const covers = (sel) => page.evaluate((s) => { const r = document.querySelector('.bcv-tour__ring').getBoundingClientRect(); const t = document.querySelector(s).getBoundingClientRect(); return r.left <= t.left + 1 && r.top <= t.top + 1 && r.right >= t.right - 1 && r.bottom >= t.bottom - 1; }, sel);
  check((await tourTitle()) === 'Your day at a glance' && /^1 of 13$/i.test((await texts('.bcv-tour__count'))[0]) && (await covers('.bcv-stat')), `stop 1 spotlights the counters (${await rects()})`);
  const next = async (title) => { await page.click('.bcv-tour__btn.is-primary'); await page.waitForFunction((t) => document.querySelector('.bcv-tour__title')?.textContent.trim() === t, title, { timeout: 15000 }); };
  await next('Everything in one place');
  check(await ringOver('.bcv-nav'), 'stop 2 spotlights the sidebar');
  await next('The smart panel');
  await next('Light or dark');
  check(await ringOver('#bcv-theme-btn'), 'stop 4 spotlights the appearance switch');
  await shot(page, '31-tour');
  await next('Your courses');
  check(page.url() === `${BASE}/courses` && (await ringOver('.bcv-ccard')), 'the tour moves to the Courses page');
  await next('To Do');
  check(page.url() === `${BASE}/#todo` && (await ringOver('.bcv-circle')), 'then To Do');
  await page.goto(`${BASE}/calendar`);
  await page.waitForSelector('.bcv-tour__pill', { timeout: 15000 });
  check(/Continue the tour · To Do/.test((await texts('.bcv-tour__pill'))[0]), 'wandering off shows a pill back to the tour');
  await page.click('.bcv-tour__pill-go');
  await page.waitForFunction(() => document.querySelector('.bcv-tour__title')?.textContent.trim() === 'To Do', null, { timeout: 15000 });
  await next('Calendar');
  await next('Term GPA');
  check(page.url() === `${BASE}/grades` && (await ringOver('.bcv-gpa__hero')), 'Grades: the term GPA');
  await next('One card per course');
  await next('Inside a course');
  check(/\/courses\/\d+$/.test(page.url()) && (await ringOver('.bcv-rail')), `a course: the rail (${page.url()})`);
  await next('Immersive Reader');
  await next('What-if scores');
  check(/\/courses\/\d+\/grades$/.test(page.url()) && (await ringOver('.bcv-whatif-btn')), 'the full grade page: what-if');
  await next('That is the tour');
  check((await page.$('.bcv-tour--center')) !== null && (await texts('.bcv-tour__btn.is-primary'))[0] === 'Done', 'the last stop is a centred card');
  await page.click('.bcv-tour__btn.is-primary');
  await page.waitForFunction(() => !document.querySelector('.bcv-tour'), null, { timeout: 5000 });
  check((await prefsOf()).tour === null && !(await page.$('html.bcv-touring')), 'Done ends the tour and clears its state');

  // ---- the page after install, and Skip --------------------------------------------------------------
  console.log('setup page');
  const setup = await context.newPage();
  const sTexts = (sel) => setup.$$eval(sel, (els) => els.map((e) => (e.innerText || e.textContent).replace(/\s+/g, ' ').trim()));
  await setup.goto(`chrome-extension://${extId}/setup/setup.html`);
  await setup.waitForSelector('.welcome .h1', { timeout: 10000 });
  const how = await sTexts('.how__t');
  check((await sTexts('.welcome .h1'))[0] === 'Simpl Courses is installed' && (await setup.$$('.blob')).length === 4 && how.join(' | ') === 'Open your Canvas | Press the puzzle piece, then Simpl Courses | Press Set up' && (await setup.$('.how__puzzle svg')) !== null && /puzzle piece at the right of the toolbar and choose Simpl Courses/.test((await sTexts('.how__s'))[1]) && (await sTexts('#next'))[0] === 'Got it' && (await setup.$('#host')) === null && (await setup.$('.progress')) === null, `the page after install says how to start, and asks nothing: ${how.join(' | ')}`);
  await setup.screenshot({ path: join(out, '32-setup-welcome.png') });
  await setup.close().catch(() => {});
  // Skip on the card: the done flags, no favourites written, no tour
  const favBefore = (await apiGet('/api/v1/courses?per_page=100')).filter((c) => c.is_favorite).length;
  await sw.evaluate(() => self.BCV.api.storage.local.remove('setup:done'));
  await page.goto(`${BASE}/grades?bcv=setup`);
  await page.waitForSelector(su('.row'), { timeout: 20000 });
  await page.click(su('.row[data-course]')); // tick one, so Skip has a change it must not write
  await page.click(su('#skip'));
  await page.waitForFunction(() => !document.querySelector('#bcv-setup'), null, { timeout: 5000 });
  await page.waitForTimeout(300);
  check(page.url() === `${BASE}/grades` && (await page.$('.bcv-tour')) === null && (await sw.evaluate(async () => (await self.BCV.api.storage.local.get('setup:done'))['setup:done'])) === true && (await apiGet('/api/v1/courses?per_page=100')).filter((c) => c.is_favorite).length === favBefore, 'Skip closes the card where it was opened, marks the setup done, writes nothing and starts no tour');
  await page.waitForSelector('.bcv-gpa__hero', { timeout: 15000 });
  check((await texts('.bcv-gpa__hero-sub'))[0]?.includes('This term so far') && (await texts('.bcv-gpa__goal-s'))[0] === 'Goal 3.90 · set it in settings', 'the Grades page tracks without a record from the setup, with the goal it set');

  // ---- the account panel ----------------------------------------------------------------------------------
  console.log('account panel');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-account', { timeout: 15000 });
  await page.click('#bcv-account');
  await page.waitForSelector('.bcv-menu--account', { timeout: 5000 });
  const acctItems = await texts('.bcv-menu--account .bcv-menu__item');
  check(acctItems.map((t) => t.split('\n')[0].trim()).join(' | ') === 'Dark appearance | Simpl Courses settings Look, courses, grades, the smart panel | Guided setup Courses, grades, the smart panel | Tour What changed, on the real pages | Canvas profile | All Canvas settings Profile, notifications, integrations | Notification preferences | Log out' && (await page.$eval('.bcv-menu--account', (m) => m.getBoundingClientRect().bottom <= window.innerHeight)), `the profile row opens a panel above itself: ${acctItems.join(' | ')}`);
  await shot(page, '34-account-panel');
  // the mock keeps the token in the _csrf_token cookie only, like Canvas (no meta tag), and its /logout
  // accepts a DELETE carrying exactly that token; anything else lands on Canvas's "Page Error"
  check(!(await page.$('meta[name="csrf-token"]')) && /_csrf_token=/.test(await page.evaluate(() => document.cookie)), 'the page carries the CSRF token in the _csrf_token cookie, not a meta tag');
  const [logoutReq] = await Promise.all([page.waitForRequest((rq) => rq.url().endsWith('/logout') && rq.method() === 'POST', { timeout: 10000 }), page.click('.bcv-menu__item--danger')]);
  const body = new URLSearchParams(logoutReq.postData() || '');
  check(body.get('_method') === 'delete' && body.get('authenticity_token') === 'mock+csrf/token=', `Log out submits Canvas's own logout form with the session's token, decoded from the cookie: ${logoutReq.postData()}`);
  await page.waitForFunction(() => /Logged out|Page Error/.test(document.body.textContent), null, { timeout: 10000 });
  check(await page.evaluate(() => /Logged out/.test(document.body.textContent) && !/Page Error/.test(document.body.textContent)), 'Canvas accepts it and logs the session out (no "Page Error")');

  // ---- never a broken card -----------------------------------------------------------------------------
  console.log('resilience');
  await mockConfig({ groupsFail: true });
  await sw.evaluate(async () => { const all = await self.BCV.api.storage.local.get(null); await self.BCV.api.storage.local.remove(Object.keys(all).filter((k) => /:groups$/.test(k))); });
  await page.goto(`${BASE}/groups`);
  await page.waitForSelector('html.bcv-punch, .bcv-error', { timeout: 20000 });
  await page.waitForTimeout(400);
  check((await page.$('html.bcv-punch')) !== null && (await page.$('.bcv-body > .bcv-error')) === null && /Showing Canvas's own page/.test((await texts('.bcv-toast')).join(' ')), `a screen that cannot load gives way to Canvas's own page, with a note: ${(await texts('.bcv-toast')).join(' | ')}`);
  await mockConfig({ groupsFail: false });
  await page.goto(`${BASE}/groups`);
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 15000 });
  // ---- notifications ------------------------------------------------------------------------------------
  console.log('notifications');
  await page.goto(`${BASE}/#notifications`);
  await page.waitForSelector('.bcv-nf__row', { timeout: 15000 });
  const nfOrder = ['Overdue', 'Due soon', 'Graded', 'Feedback', 'Announcements', 'System'];
  const nfGroups = await texts('.bcv-nf__group-t');
  const nfTotal = (await page.$$('.bcv-nf__row')).length;
  check(nfGroups.every((g, i) => i === 0 || nfOrder.indexOf(g) > nfOrder.indexOf(nfGroups[i - 1])) && ['Graded', 'Feedback', 'Announcements', 'System'].every((g) => nfGroups.includes(g)) && (await page.title()).startsWith('Notifications'), `notifications grouped by kind, in order: ${nfGroups.join(' | ')} (${nfTotal} rows)`);
  check((await texts('.bcv-head__sub'))[0] === `${nfTotal} unread · ${nfTotal} total` && (await texts('.bcv-nav')).some((t) => new RegExp(`Notifications\\s*${nfTotal}\\b`).test(t)), `the subtitle and the sidebar badge count the unread alerts: ${(await texts('.bcv-head__sub'))[0]}`);
  const graded = await texts('.bcv-nf__row[data-cat="graded"]');
  const feedback = await texts('.bcv-nf__row[data-cat="feedback"]');
  const system = await texts('.bcv-nf__row[data-cat="system"]');
  const announce = await texts('.bcv-nf__row[data-cat="announce"]');
  check(graded.some((t) => /Lec05-PreQuiz graded/.test(t) && /19 \/ 19/.test(t) && /See grades/.test(t)) && feedback.some((t) => /Joon left a comment on Dis00/.test(t) && /Good use of interval notation here/.test(t) && /Read comment/.test(t)) && system.some((t) => /Chemistry placement window closes Sep 16/.test(t) && /One attempt remaining/.test(t)) && announce.length >= 1 && announce.every((t) => /Read/.test(t)), `graded, feedback, system and announcement rows carry the Canvas facts: ${graded[0]} | ${feedback[0]} | system: ${system.join(' || ')} | announce (${announce.length}): ${announce[0]}`);
  await shot(page, '33-notifications');
  await page.click('.bcv-nf__chip[data-cat="graded"]');
  check((await texts('.bcv-nf__group-t')).join(',') === 'Graded' && (await page.$eval('.bcv-nf__chip[data-cat="graded"]', (e) => e.classList.contains('is-on'))), 'a chip narrows the list to one kind');
  await page.click('.bcv-nf__chip[data-cat="all"]');
  await page.click('.bcv-nf__row[data-cat="graded"] .bcv-nf__ib--read');
  await page.waitForFunction(() => document.querySelector('.bcv-nf__row[data-cat="graded"]')?.classList.contains('is-read'), null, { timeout: 5000 });
  check((await texts('.bcv-head__sub'))[0] === `${nfTotal - 1} unread · ${nfTotal} total` && Object.keys((await prefsOf()).notifState?.read || {}).length === 1, 'the tick marks one read: the dot goes, the count follows, the state is saved under the site');
  await page.click('#bcv-nf-unread');
  check((await page.$$('.bcv-nf__row')).length === nfTotal - 1 && (await page.$eval('#bcv-nf-unread', (e) => e.classList.contains('is-on'))), 'Unread only hides the read one');
  await page.click('#bcv-nf-unread');
  await page.click('.bcv-nf__row[data-cat="feedback"] .bcv-nf__ib--x');
  await page.waitForSelector('.bcv-nf__gone', { timeout: 5000 });
  check((await page.$$('.bcv-nf__row')).length === nfTotal - 1 && (await texts('.bcv-nf__gone'))[0].startsWith('1 dismissed') && !(await texts('.bcv-nav')).some((t) => new RegExp(`Notifications\\s*${nfTotal}\\b`).test(t)), 'Dismiss removes a row, offers Restore, and the badge drops');
  await page.click('#bcv-nf-restore');
  await page.waitForFunction(() => !document.querySelector('.bcv-nf__gone'), null, { timeout: 5000 });
  check((await page.$$('.bcv-nf__row')).length === nfTotal, 'Restore brings it back');
  await page.click('#bcv-nf-readall');
  await page.waitForFunction((n) => document.querySelector('.bcv-head__sub')?.textContent === `${n} alerts · all read`, nfTotal, { timeout: 5000 });
  check(!(await texts('.bcv-nav')).some((t) => /Notifications\s*\d/.test(t)), 'Mark all read clears the sidebar badge');
  await page.click('#bcv-nf-unread');
  check((await texts('.bcv-nf__empty'))[0] === 'Nothing unread here.', 'Unread only with nothing unread says so');
  await page.click('#bcv-nf-unread');
  // a narrow window (or a zoomed one): the column's own scale narrows the padding and the title, rows wrap
  await page.setViewportSize({ width: 760, height: 900 });
  await page.goto(`${BASE}/#notifications`);
  await page.waitForSelector('.bcv-nf__row', { timeout: 15000 });
  await page.waitForTimeout(400);
  const narrow = await page.evaluate(() => {
    const head = document.querySelector('.bcv-head');
    const row = document.querySelector('.bcv-nf__row');
    const title = row.querySelector('.bcv-nf__title').getBoundingClientRect();
    const pill = row.querySelector('.bcv-nf__course')?.getBoundingClientRect();
    const main = document.querySelector('.bcv-main').getBoundingClientRect();
    return { overflow: document.documentElement.scrollWidth > window.innerWidth + 1, padLeft: getComputedStyle(head).paddingLeft, h1: getComputedStyle(document.querySelector('.bcv-h1')).fontSize, wrapped: !!pill && pill.top > title.bottom - 2, rowRight: row.getBoundingClientRect().right <= main.right + 1, mainWidth: Math.round(main.width) };
  });
  check(!narrow.overflow && narrow.padLeft === '20px' && narrow.h1 === '28px' && narrow.wrapped && narrow.rowRight, `at 760px the column scales itself: 20px sides, a 28px title, rows wrap and nothing overflows (${JSON.stringify(narrow)})`);
  await shot(page, '33b-notifications-narrow');
  await page.setViewportSize({ width: 1400, height: 900 });
  await page.goto(`${BASE}/#notifications`);
  await page.waitForSelector('.bcv-nf__row', { timeout: 15000 });
  await clickScreen('.bcv-nf__row[data-cat="graded"] .bcv-nf__act');
  check(page.url() === `${BASE}/courses/101/assignments/1007` && !!(await page.$('.bcv-detail__title, .bcv-sb--embed')), `the action opens the item, in place: ${page.url()}`);

  // ---- extension pages ---------------------------------------------------------------------------------
  console.log('extension pages');
  const options = await context.newPage();
  const oTexts = (sel) => options.$$eval(sel, (els) => els.map((e) => (e.innerText || e.textContent).replace(/\s+/g, ' ').trim()));
  await options.goto(`chrome-extension://${extId}/options/options.html`);
  await options.waitForSelector('#skin', { timeout: 5000 });
  const navLabels = await oTexts('.navlink__label');
  check(navLabels.join(' | ') === 'General | Courses & targets | Grades | Smart panel | Appearance | Canvas sites | Data & about' && (await oTexts('#title'))[0] === 'General' && (await options.$eval('.navlink.is-active', (e) => e.dataset.section)) === 'general' && (await options.$eval('#skin', (e) => e.classList.contains('is-on'))) && !(await options.$eval('#dot-smart', (e) => e.hidden)), `settings open on General with the seven sections and an orange dot on Smart panel while no key is set: ${navLabels.join(' | ')}`);
  check(/^Version \d+\.\d+/.test(await options.$eval('#version', (el) => el.textContent)) && /^1 site$/.test((await oTexts('#statusText'))[0]), `settings show the version and the site count: ${await options.$eval('#version', (el) => el.textContent)} · ${(await oTexts('#statusText'))[0]}`);
  await options.screenshot({ path: join(out, '29-options-general.png') });
  await options.click('.navlink[data-section="smart"]');
  await options.fill('#openaiKey', 'sk-test');
  await options.dispatchEvent('#openaiKey', 'change');
  await options.waitForTimeout(300);
  check(/ChatGPT/.test(await options.$eval('#smartStatus', (el) => el.textContent)) && (await oTexts('#openaiPill'))[0] === 'Connected' && !(await options.$eval('#saved', (e) => e.hidden)) && (await options.$eval('#dot-smart', (e) => e.hidden)) && /smart panel on/.test((await oTexts('#statusText'))[0]), 'a key saves on change: Saved pill, Connected pill, status pill and the nav dot follow');
  await options.click('#testOpenAI');
  await options.waitForFunction(() => /rejected|Could not|works|Unauthorized|invalid/i.test(document.querySelector('#openaiResult').textContent), null, { timeout: 20000 });
  console.log('   key test result:', await options.$eval('#openaiResult', (el) => el.textContent));
  await options.click('#depth button[data-value="thorough"]');
  await options.waitForTimeout(200);
  check((await sw.evaluate(() => self.BCV.settings.get())).smart.depth === 'thorough' && (await options.$eval('#depth .is-on', (b) => b.textContent)) === 'Thorough' && (await oTexts('.keycard__name')).join(' | ') === 'Claude | ChatGPT | Gemini' && (await oTexts('.keycard--soon .pill'))[0] === 'Coming soon', 'the depth segment saves; Claude, ChatGPT and Gemini (coming soon) cards');
  await options.screenshot({ path: join(out, '29-options-smart.png'), fullPage: true });
  await options.fill('#openaiKey', '');
  await options.dispatchEvent('#openaiKey', 'change');
  await options.click('#depth button[data-value="balanced"]');
  // Courses & targets: read from the site the page last used, written through Canvas favourites
  await options.click('.navlink[data-section="courses"]');
  await options.waitForSelector('.course', { timeout: 15000 });
  const favCount = (await apiGet('/api/v1/courses?per_page=100')).filter((c) => c.is_favorite).length;
  check((await options.$$('.course')).length >= 8 && (await options.$$('.course .switch.is-on')).length === favCount && (await oTexts('#shownLabel'))[0] === `${favCount} of ${(await options.$$('.course')).length} courses shown`, `Courses & targets lists every active course with the ${favCount} favourites on`);
  const offRow = await options.$('.course:not(.is-off)');
  const offId = await offRow.evaluate((e) => e.dataset.course);
  await (await offRow.$('.switch')).click();
  await options.waitForFunction((id) => document.querySelector(`.course[data-course="${id}"]`)?.classList.contains('is-off'), offId, { timeout: 10000 });
  check(!(await apiGet('/api/v1/courses?per_page=100')).find((c) => String(c.id) === offId).is_favorite, 'hiding a course removes it from the Canvas favourites');
  await (await options.$(`.course[data-course="${offId}"] .switch`)).click();
  await options.waitForFunction((id) => !document.querySelector(`.course[data-course="${id}"]`)?.classList.contains('is-off'), offId, { timeout: 10000 });
  await options.click(`.course[data-course="${offId}"] .seg button[data-value="B+"]`);
  await options.waitForTimeout(300);
  check((await apiGet('/api/v1/courses?per_page=100')).find((c) => String(c.id) === offId).is_favorite && (await prefsOf()).gradeTargets?.[offId] === 'B+' && (await options.$$eval('.course:first-child .seg button', (bs) => bs.map((b) => b.textContent))).join(' ') === 'C B B+ A- A A+', 'showing it again restores the favourite, and the letter writes the target the Grades page reads; the letters run low to high with A+ on the right');
  // a nickname typed here is written to Canvas with the token the Canvas page remembered for this page
  const nickInput = options.locator(`.course[data-course="${offId}"] .course__nick`);
  await nickInput.fill('Settings nick');
  await nickInput.dispatchEvent('change');
  await options.waitForFunction(() => !document.querySelector('#saved').hidden, null, { timeout: 5000 }).catch(() => {});
  const viaSettings = (await apiGet('/api/v1/courses?per_page=100')).find((c) => String(c.id) === offId);
  check(viaSettings.name === 'Settings nick' && !!viaSettings.original_name, `a nickname typed in Settings reaches Canvas: ${viaSettings.original_name} → ${viaSettings.name}`);
  await nickInput.fill('');
  await nickInput.dispatchEvent('change');
  await options.waitForTimeout(400);
  check(!(await apiGet('/api/v1/courses?per_page=100')).find((c) => String(c.id) === offId).original_name, 'clearing the field removes the nickname');
  await options.screenshot({ path: join(out, '29-options-courses.png'), fullPage: true });
  // Grades: the same preferences as the Grades page
  await options.click('.navlink[data-section="grades"]');
  check((await oTexts('#gpaGoal'))[0] === '3.90' && (await options.$eval('#tracking', (e) => e.classList.contains('is-on'))) && /recorded|from today/i.test((await oTexts('#historyLabel'))[0]), `Grades shows the goal and tracking the setup chose: ${(await oTexts('#historyLabel'))[0]}`);
  await options.click('#whatIf');
  await options.click('#goalUp');
  await options.waitForTimeout(500);
  const gp = await prefsOf();
  check(gp.whatIfScores === false && gp.gpaGoal === 3.95, 'what-if off and the goal step save under the site');
  await options.click('#whatIf');
  await options.waitForTimeout(400);
  // Appearance: theme tiles
  await options.click('.navlink[data-section="appearance"]');
  await options.click('.theme[data-value="on"]');
  await options.waitForTimeout(250);
  check((await sw.evaluate(() => self.BCV.settings.get())).appearance.darkMode === 'on' && (await options.$eval('html', (e) => e.dataset.theme)) === 'dark' && (await options.$eval('.theme.is-on', (b) => b.dataset.value)) === 'on', 'a theme tile saves and repaints the page');
  await options.screenshot({ path: join(out, '29-options-dark.png') });
  await options.click('.theme[data-value="system"]');
  await options.waitForTimeout(250);
  // Appearance: and where the courses sit, the same choice the last step of the setup offers
  check((await options.$$eval('#sideCourses button', (bs) => bs.map((b) => b.textContent))).join(',') === 'Always listed,On hover' && (await options.$eval('#sideCourses button.is-on', (b) => b.dataset.value)) === 'always', 'the courses in the sidebar: listed or on hover, listed to begin with');
  await options.click('#sideCourses button[data-value="hover"]');
  await options.waitForTimeout(250);
  check((await sw.evaluate(() => self.BCV.settings.get())).appearance.sideCourses === 'hover' && (await options.$eval('#sideCourses button.is-on', (b) => b.dataset.value)) === 'hover', 'changing it saves');
  await options.click('#sideCourses button[data-value="always"]');
  await options.waitForTimeout(250);
  // Sites and data
  await options.click('.navlink[data-section="sites"]');
  check((await oTexts('.site__host'))[0] === '*.instructure.com' && (await options.$eval('#addDomain', (b) => b.disabled)), 'Canvas sites lists the built-in host; Add waits for an address');
  await options.fill('#newDomain', 'canvas.school');
  check(!(await options.$eval('#addDomain', (b) => b.disabled)), 'an address with a dot enables Add');
  await options.click('.navlink[data-section="data"]');
  await options.waitForSelector('.stat', { timeout: 5000 });
  check((await oTexts('.stat')).length === 5 && (await oTexts('.stat b'))[0] === 'none' && (await oTexts('.action .row__t')).join(' | ') === 'Clear smart panel conversations | Export settings | Import settings | Reset everything', `Data & about: ${(await oTexts('.stat')).join(' | ')}`);
  // the section ends with the uninstall steps for the browser this page is open in (Chromium here)
  const unSteps = await oTexts('#uninstallSteps li');
  check((await oTexts('#uninstall .row__t'))[0] === 'Uninstalling' && unSteps.length === 2 && /^Press Reset everything above/.test(unSteps[0]) && /chrome:\/\/extensions/.test(unSteps[1]) && /Remove/.test(unSteps[1]) && /Canvas account, favourites and course nicknames live on Canvas/.test((await oTexts('#uninstallNote'))[0]), `Data & about explains uninstalling for this browser: ${unSteps.join(' | ').slice(0, 140)}`);
  await options.fill('#query', 'logo');
  check((await options.$$eval('.navlink', (els) => els.filter((e) => !e.hidden).map((e) => e.dataset.section))).join(',') === 'appearance' && (await oTexts('#title'))[0] === 'Appearance', 'search narrows the sections and opens the match');
  await options.fill('#query', '');
  await options.click('.navlink[data-section="general"]');
  check((await options.$('#openSetup')) !== null && (await options.$('#runTour')) !== null, 'General offers Reopen setup and Run the tour again');
  // before the guided setup has run (or been skipped) the popup is nothing but a setup button
  await sw.evaluate(async () => {
    const k = 'prefs:localhost:8787';
    const all = await self.BCV.api.storage.local.get(k);
    const p = all[k] || {};
    delete p.setupDone;
    await self.BCV.api.storage.local.set({ [k]: p });
    await self.BCV.api.storage.local.remove('setup:done');
  });
  let popupPage = await context.newPage();
  await popupPage.goto(`chrome-extension://${extId}/popup/popup.html`);
  await popupPage.waitForTimeout(500);
  const hiddenIn = (sel) => popupPage.$eval(sel, (el) => getComputedStyle(el).display === 'none');
  check(!(await popupPage.$eval('#setup-card', (el) => el.hidden)) && (await hiddenIn('.section')) && (await hiddenIn('.foot')) && (await hiddenIn('#open-settings')) && (await popupPage.$eval('#status', (el) => el.textContent)) === 'Not set up yet', 'before setup the popup shows only the setup button');
  await popupPage.screenshot({ path: join(out, '30-popup-fresh.png') });
  check((await popupPage.$eval('#start-setup', (b) => b.textContent)) === 'Set up', 'the button says Set up');
  await popupPage.click('#start-setup');
  await popupPage.waitForTimeout(300);
  check((await popupPage.$eval('#setup-msg', (e) => e.textContent)) === 'Open your Canvas courses page first, then press Set up.' && (await sw.evaluate(async () => (await self.BCV.api.storage.local.get('setup:done'))['setup:done'])) === undefined, 'off a Canvas tab, Set up says where to go and nothing is marked done');
  // only this flow's own flag counts: preferences from the older in-page setup do not
  await sw.evaluate(async () => { const k = 'prefs:localhost:8787'; const all = await self.BCV.api.storage.local.get(k); await self.BCV.api.storage.local.set({ [k]: { ...(all[k] || {}), setupDone: true } }); });
  popupPage = await context.newPage();
  await popupPage.goto(`chrome-extension://${extId}/popup/popup.html`);
  await popupPage.waitForTimeout(400);
  check(!(await popupPage.$eval('#setup-card', (el) => el.hidden)), 'an older setup mark alone does not count: the popup still asks');
  await sw.evaluate(() => self.BCV.api.storage.local.set({ 'setup:done': true }));
  popupPage = await context.newPage();
  await popupPage.goto(`chrome-extension://${extId}/popup/popup.html`);
  await popupPage.waitForTimeout(500);
  check((await popupPage.$eval('#setup-card', (el) => el.hidden)) && !(await hiddenIn('.section')), 'after setup the popup shows the switches again');
  check(/^v\d+\.\d+/.test(await popupPage.$eval('#version', (el) => el.textContent)), `popup shows the version: ${await popupPage.$eval('#version', (el) => el.textContent)}`);
  check((await popupPage.$eval('#foot-setup', (el) => el.textContent)) === 'Guided setup', 'the popup links to the guided setup');
  await popupPage.screenshot({ path: join(out, '30-popup.png') });

  // ---- Reset everything reaches the site: the one-line note a Canvas tab keeps in its own storage goes too ----
  console.log('reset');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 10000 });
  check((await page.evaluate(() => localStorage.getItem('bcv:early'))) !== null, 'a Canvas tab keeps the early note (look + appearance) in its own site storage');
  await options.bringToFront();
  await options.click('.navlink[data-section="data"]');
  options.once('dialog', (d) => d.accept());
  await options.click('#resetSettings');
  await page.waitForFunction(() => localStorage.getItem('bcv:early') === null, null, { timeout: 5000 }).catch(() => {});
  const afterReset = await sw.evaluate(async () => Object.keys(await self.BCV.api.storage.local.get(null)));
  check((await page.evaluate(() => localStorage.getItem('bcv:early'))) === null && !afterReset.some((k) => k.startsWith('prefs:') || k.startsWith('smart:')), `Reset everything clears the note on the open Canvas tab and the extension's own storage (left: ${afterReset.join(', ') || 'nothing'})`);
} catch (e) {
  console.error('smoke test crashed:', e?.stack || e);
  failures.push('crash: ' + e.message);
  // what the page was doing when it crashed (a wait that timed out is easier to read with this)
  const where = await page?.evaluate(() => ({ url: location.href, settled: document.documentElement.classList.contains('bcv-settled'), loads: document.querySelectorAll('.bcv-load').length, loadKey: window.BCV?.app?.state?.loadKey ?? null, route: window.BCV?.app?.state?.route?.screen ?? null, h1: document.querySelector('#bcv-main h1')?.textContent?.trim() ?? null, main: document.querySelector('#bcv-main > *')?.className ?? null, toasts: [...document.querySelectorAll('.bcv-toast')].map((t) => t.textContent.trim()) })).catch((e2) => ({ unreadable: e2.message }));
  console.error('page state at the crash:', JSON.stringify(where));
} finally {
  await context.close();
  server.kill();
  rmSync(extDir, { recursive: true, force: true });
  rmSync(userDataDir, { recursive: true, force: true });
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);

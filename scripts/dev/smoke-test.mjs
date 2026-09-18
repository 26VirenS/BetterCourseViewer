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

// 4. every release ships its What's New notes (content/app/whatsnew-notes.js): the first Canvas
// page after an update shows them, so a version without an entry is a version with nothing to say
const cmpVer = (a, b) => { const x = String(a).split('.').map(Number); const y = String(b).split('.').map(Number); for (let i = 0; i < Math.max(x.length, y.length); i++) { const d = (x[i] || 0) - (y[i] || 0); if (d) return d; } return 0; };
const whatsNew = new Function('self', `${readFileSync(join(extDir, 'content', 'app', 'whatsnew-notes.js'), 'utf8')}; return self.BCV_WHATS_NEW;`)({});
{
  console.log("\nwhat's new notes");
  const newest = whatsNew[0];
  check(!!newest && newest.version === manifest.version, `the newest What's New entry is this version (${manifest.version}): ${newest?.version}`);
  check(/^\d{4}-\d{2}-\d{2}$/.test(newest?.date || '') && Array.isArray(newest?.notes) && newest.notes.length > 0, `the notes for ${manifest.version} are dated and not empty`);
  const bad = whatsNew.flatMap((v) => (v.notes || []).map((n) => ({ v: v.version, ...n }))).filter((n) => !['new', 'improved', 'fixed'].includes(n.kind) || !n.title || n.title.length > 30 || !n.body || n.body.length > 90 || !n.icon);
  check(bad.length === 0, `every note has a kind, a short title, one short line and an icon${bad.length ? `: ${bad.map((n) => `${n.v} · ${n.title}`).join(' | ')}` : ''}`);
  check(whatsNew.every((v, i) => i === 0 || cmpVer(whatsNew[i - 1].version, v.version) > 0), 'the entries are newest first, no version twice');
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
  check((await sw.evaluate(async () => (await self.BCV.api.storage.local.get('setup:flow'))['setup:flow'])) === 3, 'the build records its setup flow, so an update from an older flow offers the page once more');
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
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:done': true, 'whatsnew:seen': v }), manifest.version); // (seen: What's new is driven on purpose below, not over every page)
  check(!(await offerAgain()), 'once setup is done it never opens again');
  // the setup counts as done for the rest of the suite (until it is done, every page opens the card
  // — the guided-setup sections below clear the flag when that is what they are checking)
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'whatsnew:seen': v }), manifest.version);

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
  const lookBtn = await page.$eval('#bcv-look', (e) => { const r = e.getBoundingClientRect(); const m = e.querySelector('.bcv-look__main'); return { fixed: getComputedStyle(e).position === 'fixed', top: Math.round(r.top), right: Math.round(window.innerWidth - r.right), w: Math.round(r.width), h: Math.round(r.height), on: m.getAttribute('aria-checked'), text: m.querySelector('.bcv-look__text').textContent.trim(), textShown: m.querySelector('.bcv-look__text').getBoundingClientRect().width, persistShown: e.querySelector('.bcv-look__persist').getBoundingClientRect().height }; }).catch(() => null);
  check(!!lookBtn && lookBtn.fixed && lookBtn.top < 40 && lookBtn.right < 24 && lookBtn.w <= 60 && lookBtn.h <= 26 && lookBtn.on === 'true' && lookBtn.text === 'Simpl Courses' && lookBtn.textShown === 0 && lookBtn.persistShown === 0, `the look switch sits at the top right of the page, on, folded to the mark and the switch: ${JSON.stringify(lookBtn)}`);
  // under the pointer the name comes out and a second row opens: Persistent, the same switch the popup has
  await page.hover('#bcv-look');
  check(await eventually(() => page.$eval('#bcv-look', (e) => e.getBoundingClientRect().width > 120 && e.querySelector('.bcv-look__text').getBoundingClientRect().width > 40 && e.querySelector('.bcv-look__persist').getBoundingClientRect().height > 20 && getComputedStyle(e.querySelector('.bcv-look__persist')).opacity === '1')), 'hovering the switch brings out the name and opens the Persistent row');
  const persistRow = await page.$eval('#bcv-look .bcv-look__persist', (e) => ({ on: e.getAttribute('aria-checked'), text: e.textContent.trim(), hint: e.querySelector('.bcv-look__phint') !== null }));
  check(persistRow.on === 'false' && persistRow.text === 'Persistent' && !persistRow.hint, `the row reads the setting, off to begin with, and says nothing more: ${JSON.stringify(persistRow)}`);
  await page.click('#bcv-look .bcv-look__persist');
  check(await eventually(async () => (await sw.evaluate(async () => (await self.BCV.settings.get()).appearance.persistLook)) === true) && (await page.$eval('#bcv-look .bcv-look__persist', (e) => e.getAttribute('aria-checked'))) === 'true' && (await visible('#bcv-app')), 'pressing it turns Persistent on — saved, the switch shows it, and nothing reloads');
  await page.click('#bcv-look .bcv-look__persist');
  check(await eventually(async () => (await sw.evaluate(async () => (await self.BCV.settings.get()).appearance.persistLook)) === false) && (await page.$eval('#bcv-look .bcv-look__persist', (e) => e.getAttribute('aria-checked'))) === 'false', 'and off again');
  await shot(page, '01d-look-switch-open');
  await page.mouse.move(700, 500);
  check(await eventually(() => page.$eval('#bcv-look', (e) => e.querySelector('.bcv-look__persist').getBoundingClientRect().height === 0 && e.getBoundingClientRect().width <= 60)), 'the row folds away, and the name with it, when the pointer leaves');
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
  // a sheet row previews inside the sheet: it widens and the preview takes its right, the list staying
  const sheetW0 = await page.$eval(".bcv-sheet", (e) => Math.round(e.getBoundingClientRect().width));
  await page.click('.bcv-sheet__row');
  await page.waitForSelector('.bcv-sheet.is-split .bcv-pv--in', { timeout: 10000 });
  await page.waitForFunction(() => !document.querySelector('.bcv-pv .bcv-skel'), null, { timeout: 10000 });
  const pvSheet = await page.$eval('.bcv-sheet', (e) => ({
    kicker: e.querySelector('.bcv-pv__kicker').textContent,
    title: e.querySelector('.bcv-pv__title').textContent,
    meta: e.querySelector('.bcv-pv__meta').textContent,
    body: e.querySelector('.bcv-pv__prose')?.textContent.slice(0, 40) || '',
    go: e.querySelector('.bcv-pv__go').textContent,
    rows: e.querySelectorAll('.bcv-sheet__row').length,
  }));
  check(pvSheet.kicker === 'Preview' && pvSheet.title === 'Field site sign-ups' && /^Announcement · .+/.test(pvSheet.meta) && pvSheet.body.length > 10 && pvSheet.go === 'Open the announcement' && pvSheet.rows === 3, `the preview reads the announcement beside the list it came from: ${JSON.stringify(pvSheet)}`);
  await page.waitForTimeout(400); // the sheet takes .3s to make room
  const split = await page.evaluate(() => {
    const sheet = document.querySelector('.bcv-sheet').getBoundingClientRect();
    const pv = document.querySelector('.bcv-pv--in').getBoundingClientRect();
    const list = document.querySelector('.bcv-sheet__list').getBoundingClientRect();
    return { w: Math.round(sheet.width), pvRight: Math.round(pv.right), sheetRight: Math.round(sheet.right), pvLeft: Math.round(pv.left), listRight: Math.round(list.right), shifted: document.documentElement.classList.contains('bcv-preview') };
  });
  check(split.w > sheetW0 && Math.abs(split.pvRight - split.sheetRight) <= 2 && split.pvLeft >= split.listRight - 2 && !split.shifted, `the sheet grew and the preview sits on its right, the page itself never moving: ${JSON.stringify({ ...split, sheetW0 })}`);
  await shot(page, '01c-dashboard-preview');
  // pressing another row swaps what the panel shows, the sheet staying put
  await (await page.$$('.bcv-sheet__row'))[1].click();
  await page.waitForFunction(() => document.querySelector('.bcv-pv__title')?.textContent === 'Prerequisite Skills Test', null, { timeout: 10000 });
  check((await page.$$('.bcv-pv')).length === 1 && (await page.$('.bcv-sheet.is-split')) !== null, 'another row swaps the preview inside the sheet');
  // the button at the bottom is the way through to the item's own screen, and it takes the sheet with it
  await (await page.$$('.bcv-sheet__row'))[0].click();
  await page.waitForFunction(() => document.querySelector('.bcv-pv__title')?.textContent === 'Field site sign-ups', null, { timeout: 10000 });
  await page.click('.bcv-pv__go');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(page.url().endsWith('/courses/104/announcements/8005') && !(await page.$('.bcv-pv')) && !(await page.$('.bcv-sheet')), 'the button at the bottom of the preview opens the item and closes the sheet');
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
  // a list row previews the item beside the dashboard rather than navigating to it
  const assignRow = await page.$('.bcv-day a.bcv-row[href*="/assignments/"]');
  const assignHref = await assignRow.getAttribute('href');
  await assignRow.click({ position: { x: 220, y: 22 } }); // past the tick circle, on the row body
  await page.waitForFunction(() => document.querySelector('.bcv-pv') && !document.querySelector('.bcv-pv .bcv-skel'), null, { timeout: 10000 });
  const pvRow = await page.$eval('.bcv-pv', (e) => ({
    meta: e.querySelector('.bcv-pv__meta').textContent,
    chips: [...e.querySelectorAll('.bcv-pv__chips .bcv-badge')].map((b) => b.textContent),
    has: !!e.querySelector('.bcv-pv__prose, .bcv-pv__none'),
    go: e.querySelector('.bcv-pv__go').textContent,
  }));
  check(page.url().endsWith('/') && /^Assignment · (due .+|no due date)( · \d+(\.\d+)? pts)?$/.test(pvRow.meta) && pvRow.chips.length >= 1 && /^(Graded|Submitted|Submitted late|Missing|Not submitted|Excused)$/.test(pvRow.chips[0]) && pvRow.has && pvRow.go === 'Open the assignment', `a list row previews the assignment without navigating: ${JSON.stringify(pvRow)}`);
  check(!/null|NaN|undefined/.test(await page.$eval('.bcv-pv', (e) => e.textContent)), 'the preview never shows a raw null');
  // nothing is covered or dimmed: the interface stays live beside the panel, so pressing another
  // item we can read swaps what the panel shows rather than putting it away
  const wasTitle = await page.$eval('.bcv-pv__title', (e) => e.textContent);
  const nextRow = (await page.$$(`.bcv-day a.bcv-row[href*="/assignments/"]:not([href="${assignHref}"])`))[0];
  await nextRow.click({ position: { x: 220, y: 22 } });
  await page.waitForFunction((was) => document.querySelector('.bcv-pv__title')?.textContent !== was && !document.querySelector('.bcv-pv .bcv-skel'), wasTitle, { timeout: 10000 });
  const pvSwap = await page.$eval('.bcv-pv__meta', (e) => e.textContent);
  check((await page.$$('.bcv-pv')).length === 1 && /^Assignment · /.test(pvSwap) && (await page.$eval('html', (e) => e.classList.contains('bcv-preview'))), `pressing another row swaps the preview in place, the interface never moving back: ${pvSwap}`);
  // a press anywhere else puts it away, and goes through to what was pressed
  await page.click('.bcv-head .bcv-h1');
  await page.waitForFunction(() => !document.querySelector('.bcv-pv') && !document.documentElement.classList.contains('bcv-preview'), null, { timeout: 3000 });
  check(true, 'a press on the interface closes the preview');
  // only the kinds we can read are previewed; everything else navigates the way it always did
  const pvRoutes = await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    const [{ result }] = await chrome.scripting.executeScript({
      target: { tabId: tab.id }, world: 'ISOLATED',
      func: () => [
        '/courses/101/assignments/9001', '/courses/101/quizzes/5001', '/courses/101/discussion_topics/7001',
        '/courses/101/announcements/8001', '/courses/101/pages/week-1', '/groups/201/discussion_topics/7001',
        '/courses/101/assignments', '/courses/101/modules', '/courses/101', '/conversations',
        '/courses/101/files/3001', '/groups/201/assignments/9001', '/courses/101/quizzes/5001?bcv=take',
      ].map((u) => `${u}:${self.BCV.preview.previewable(u) ? 'look' : 'go'}`),
    });
    return result;
  }, BASE);
  check(pvRoutes.filter((r) => r.endsWith(':look')).length === 6 && pvRoutes.slice(0, 6).every((r) => r.endsWith(':look')) && pvRoutes.slice(6).every((r) => r.endsWith(':go')), `only readable items are previewed: ${pvRoutes.join(' | ')}`);
  // navigating away takes the preview with it
  await (await page.$('.bcv-day a.bcv-row[href*="/assignments/"]')).click({ position: { x: 220, y: 22 } });
  await page.waitForSelector('.bcv-pv', { timeout: 10000 });
  check(assignHref.includes('/assignments/') && (await page.$eval('.bcv-pv', (e) => e.parentElement.tagName)) === 'BODY', 'the preview stands over the page, beside the dashboard it came from');
  await page.click('.bcv-nav__item[data-nav="courses"]');
  await page.waitForSelector('[data-term]', { timeout: 10000 });
  check(!(await page.$('.bcv-pv')) && !(await page.$eval('html', (e) => e.classList.contains('bcv-preview'))), 'navigating away closes the preview');
  await page.click('.bcv-nav__item[data-nav="dashboard"]');
  await page.waitForSelector('.bcv-day .bcv-row', { timeout: 10000 });
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
  // an activity row previews too; reading it there counts as opening it, so its dot clears
  await (await page.$$('.bcv-body .bcv-row--top'))[1].click();
  await page.waitForFunction(() => document.querySelector('.bcv-pv') && !document.querySelector('.bcv-pv .bcv-skel'), null, { timeout: 10000 });
  const pvAct = await page.$eval('.bcv-pv', (e) => `${e.querySelector('.bcv-pv__title').textContent} | ${e.querySelector('.bcv-pv__meta').textContent} | ${e.querySelector('.bcv-pv__go').textContent}`);
  check(/ \| Discussion · .+ \| Open the discussion$/.test(pvAct) && (await dots()) === 2, `an activity row previews the discussion and loses its dot: ${pvAct}`);
  await page.click('.bcv-pv__go');
  await page.waitForSelector('.bcv-entry', { timeout: 10000 });
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-act__title', { timeout: 10000 });
  // the announcement's own read state arrives with the feed, a moment after the list is drawn
  check(await eventually(async () => (await dots()) === 2), `opening a stream item clears its dot: ${await dots()} left`);
  await page.click('.bcv-seg__btn[data-value="list"]');
  // one small button at the top of the list hides what is already done, and says how much it hid
  await page.waitForSelector('.bcv-dash__done', { timeout: 10000 });
  const dashDoneRows = () => page.$$eval('.bcv-day .bcv-row', (els) => els.filter((e) => e.classList.contains('bcv-row--done')).length);
  const dashAllRows = () => page.$$eval('.bcv-day .bcv-row', (els) => els.length);
  const [doneBefore, allBefore] = [await dashDoneRows(), await dashAllRows()];
  check(doneBefore > 0 && (await texts('.bcv-dash__done'))[0] === 'Hide completed' && (await page.$eval('.bcv-dash__done', (e) => e.getBoundingClientRect().height <= 32)), `the list shows its done rows, ticked, with a small Hide completed above them (${doneBefore} of ${allBefore} done)`);
  await shot(page, '01d-dashboard-hide-done');
  await page.click('.bcv-dash__done');
  check(await eventually(async () => (await dashDoneRows()) === 0 && (await dashAllRows()) === allBefore - doneBefore), 'Hide completed takes exactly the done rows out');
  check((await texts('.bcv-dash__done'))[0] === `Show completed · ${doneBefore}` && (await page.$eval('.bcv-dash__done', (e) => e.classList.contains('is-on'))), `and the button says how many it is hiding: ${(await texts('.bcv-dash__done'))[0]}`);
  await page.reload();
  await page.waitForSelector('.bcv-dash__done', { timeout: 15000 });
  check(await eventually(async () => (await texts('.bcv-dash__done'))[0] === `Show completed · ${doneBefore}`), 'the choice is kept across a reload');
  await page.click('.bcv-dash__done');
  check(await eventually(async () => (await dashDoneRows()) === doneBefore), 'Show completed brings them back');
  // the views the dashboard offers are the ones the setup (or Settings) asked for; one alone needs no switcher
  await setSettings({ appearance: { dashboard: { activity: false } } });
  await page.reload();
  await page.waitForSelector('.bcv-head .bcv-seg__btn', { timeout: 15000 });
  check((await page.$$eval('.bcv-head .bcv-seg__btn', (bs) => bs.map((b) => b.dataset.value))).join(',') === 'cards,list', `a view switched off in the settings is not offered: ${(await page.$$eval('.bcv-head .bcv-seg__btn', (bs) => bs.map((b) => b.dataset.value))).join(',')}`);
  await setSettings({ appearance: { dashboard: { cards: false, activity: false } } });
  await page.reload();
  await page.waitForSelector('.bcv-day', { timeout: 15000 });
  check(!(await page.$('.bcv-head .bcv-seg__btn')) && !!(await page.$('.bcv-day')), 'with one view left there is no switcher, and the dashboard is that view');
  await setSettings({ appearance: { dashboard: { cards: true, list: true, activity: true } } });
  await page.reload();
  await page.waitForSelector('.bcv-head .bcv-seg__btn', { timeout: 15000 });

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
  // Two tabs. The site's preferences are one object; a second tab that loaded its copy earlier and
  // then writes a preference of its own must not put that older copy back over a priority set here.
  const tabB = await context.newPage();
  await tabB.goto(`${BASE}/`);
  await tabB.waitForSelector('.bcv-stat', { timeout: 15000 }); // tab B has read the preferences by now
  const secondWork = page.locator('.bcv-row', { hasNot: page.locator('.bcv-todo__del') }).filter({ has: page.locator('.bcv-pri') }).nth(1);
  const secondId = await secondWork.getAttribute('data-item');
  await secondWork.locator('.bcv-pri').click();
  await page.waitForSelector('.bcv-menu', { timeout: 3000 });
  await page.click('.bcv-menu__item:nth-child(1)'); // High, set in tab A after tab B loaded
  await page.waitForFunction((id) => document.querySelector(`.bcv-row[data-item="${id}"] .bcv-pri`)?.textContent.trim() === 'High', secondId, { timeout: 5000 });
  await tabB.click('.bcv-nav__item[data-nav="todo"]');
  await tabB.waitForSelector('.bcv-todo__done', { timeout: 15000 });
  await tabB.click('.bcv-todo__done'); // tab B writes a preference of its own (show completed)
  await tabB.waitForSelector('.bcv-body .bcv-row--done', { timeout: 5000 });
  const priAfterB = (await sw.evaluate(async () => Object.entries(await chrome.storage.local.get(null)).find(([k]) => k.startsWith('prefs:'))?.[1]));
  check(priAfterB?.todoPriority?.[workId] === 1 && priAfterB?.todoPriority?.[secondId] === 3 && priAfterB?.todoShowDone === true, `a preference written in another tab keeps the priorities set here (both tabs' changes are in storage): ${JSON.stringify(priAfterB?.todoPriority)}, showDone ${priAfterB?.todoShowDone}`);
  await tabB.click('.bcv-todo__done'); // and back, so the later checks start from hidden
  await tabB.waitForFunction(() => !document.querySelector('.bcv-body .bcv-row--done'), null, { timeout: 5000 });
  await tabB.close();
  await page.reload();
  await page.waitForSelector('.bcv-row[data-item] .bcv-pri', { timeout: 10000 });
  check((await page.$eval(`.bcv-row[data-item="${secondId}"] .bcv-pri`, (e) => e.textContent.trim())) === 'High' && (await page.$eval(`.bcv-row[data-item="${workId}"] .bcv-pri`, (e) => e.textContent.trim())) === 'Low', 'and both priorities are still on the rows after a reload');
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
  // A task of your own stays on the list until it is done, whatever its date: one from three days
  // ago and one for three weeks on are both there (course work keeps to the seven-day window).
  const noteApi = (method, path, body) => fetch(`${BASE}${path}`, { method, headers: { 'content-type': 'application/json', 'x-csrf-token': 'mock+csrf/token=' }, body: body ? JSON.stringify(body) : undefined }).then((r) => r.text()).then((t) => JSON.parse(t.replace(/^while\(1\);/, ''))); // the mock, like Canvas, refuses a write without the session's token
  const oldNote = await noteApi('POST', '/api/v1/planner_notes', { title: 'Return the library book', todo_date: new Date(Date.now() - 3 * 864e5).toISOString() });
  const farNote = await noteApi('POST', '/api/v1/planner_notes', { title: 'Book the dentist', todo_date: new Date(Date.now() + 20 * 864e5).toISOString() });
  await page.reload();
  await page.waitForFunction(() => [...document.querySelectorAll('.bcv-group__head')].some((e) => /^My tasks/.test(e.textContent)), null, { timeout: 10000 });
  const mineTitles = await page.evaluate(() => { const head = [...document.querySelectorAll('.bcv-group__head')].find((e) => /^My tasks/.test(e.textContent)); return [...head.parentElement.querySelectorAll('.bcv-row__title')].map((e) => e.textContent.trim()); });
  await page.waitForFunction((n) => Number(document.querySelector('.bcv-nav__item[data-nav="todo"] .bcv-nav__count').textContent) === n + 2, badgeBefore, { timeout: 5000 });
  check(mineTitles.join(' | ') === 'Return the library book | Book the dentist' && (await texts('.bcv-group__head')).filter((t) => /^My tasks/.test(t)).length === 1, `a task from three days ago and one three weeks out both stay under My tasks, oldest first, and the badge counts them: ${mineTitles.join(' | ')}`);
  await noteApi('DELETE', `/api/v1/planner_notes/${oldNote.id}`);
  await noteApi('DELETE', `/api/v1/planner_notes/${farNote.id}`);
  await page.reload();
  await page.waitForFunction((n) => Number(document.querySelector('.bcv-nav__item[data-nav="todo"] .bcv-nav__count').textContent) === n, badgeBefore, { timeout: 10000 });
  await page.waitForSelector('.bcv-todo__add', { timeout: 10000 });
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
  // leaving: the group rings sweep back out, last in first out, their tracks fading; the letter comes back once they have gone
  const outAnim = await page.evaluate(() => [...document.querySelectorAll('.bcv-gpa__card .bcv-ring--unfill')].map((e) => `${getComputedStyle(e).animationName}@${getComputedStyle(e).animationDelay}@${getComputedStyle(e).animationFillMode}`));
  const outTracks = await page.evaluate(() => [...document.querySelectorAll('.bcv-gpa__card .bcv-ring--track-out')].map((e) => getComputedStyle(e).animationName));
  check(outAnim.length >= 2 && outAnim.every((a, i) => a === `bcv-ring-unfill@${Number(((outAnim.length - 1 - i) * 60) / 1000)}s@forwards`) && outTracks.length === outAnim.length && outTracks.every((a) => a === 'bcv-fade-out') && (await page.$eval('.bcv-gpa__card .bcv-gpa__ringletter', (e) => e.hidden)), `leaving the ring sweeps the group rings back out, last in first, tracks fading, the letter still away: ${outAnim.join(',')}`);
  await page.waitForFunction(() => !document.querySelector('.bcv-gpa__card .bcv-ring--unfill') && !document.querySelector('.bcv-gpa__card .bcv-gpa__ringletter').hidden, null, { timeout: 3000 });
  check((await page.$eval('.bcv-gpa__card .bcv-gpa__ringletter', (e) => getComputedStyle(e).animationName)) === 'bcv-fade-in' && (await page.$$('.bcv-gpa__card .bcv-gpa__cats circle')).length === 0, 'and once they have gone the rings are cleared and the letter fades back in');
  await shot(page, '09d-grades-panel');
  // Details: the course's grade page in a sheet, with the target stepper
  await page.click('.bcv-gpa__card .bcv-gpa__details');
  await page.waitForSelector('.bcv-gpa-detail', { timeout: 5000 });
  const detail = (await texts('.bcv-gpa-detail'))[0];
  check(/^F26-MATH 021 20 MATH-021-20 92\.4% A− Target − A− · 90% \+ by group/i.test(detail) && /how the grade is weighted 100% of final grade/i.test(detail) && /Midterms 57% · nothing graded/.test(detail) && (await page.$$('.bcv-gpa-detail__arow')).length === 17 && (await page.$$('.bcv-gpa-detail .bcv-wbar__seg--ungraded')).length === 2, `details sheet: ${detail.slice(0, 200)}`);
  // the Grades page lists the same assignments, and they open the same way the course page's do
  const gpaRow = await page.$eval('.bcv-gpa-detail__arow', (e) => ({ tag: e.tagName, href: e.getAttribute('href'), name: e.querySelector('.bcv-gpa-detail__aname')?.textContent }));
  check(gpaRow.tag === 'A' && /\/courses\/\d+\/assignments\/\d+$/.test(gpaRow.href || ''), `a row on the Grades page opens the assignment it is about: ${JSON.stringify(gpaRow)}`);
  await page.click('.bcv-gpa-detail__arow');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(/\/courses\/\d+\/assignments\/\d+$/.test(page.url()) && (await texts('.bcv-detail__title'))[0] === gpaRow.name && !(await page.$('.bcv-sheet-ov')), `and it lands there with the sheet put away: ${page.url()}`);
  await page.goBack();
  await page.waitForSelector('.bcv-gpa__card .bcv-gpa__details', { timeout: 15000 });
  await page.click('.bcv-gpa__card .bcv-gpa__details');
  await page.waitForSelector('.bcv-gpa-detail', { timeout: 5000 });
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
  // The mark is a chip in the title's own row, and it opens the submission sheet (handoff surface 1)
  await page.goto(`${BASE}/courses/104/assignments/4001`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  const markBox = await page.evaluate(() => {
    const g = document.querySelector('.bcv-detail__grade'), t = document.querySelector('.bcv-detail__title');
    const gr = g.getBoundingClientRect(), tr = t.getBoundingClientRect(), hr = g.parentElement.getBoundingClientRect();
    return { tag: g.tagName, inHead: g.parentElement === t.parentElement, rightOfTitle: Math.round(gr.left - tr.right) >= 8, insetRight: Math.round(hr.right - gr.right) <= 1, size: Math.round(parseFloat(getComputedStyle(g.querySelector('.bcv-detail__gradescore')).fontSize)) };
  });
  check(markBox.tag === 'BUTTON' && markBox.inHead && markBox.rightOfTitle && markBox.insetRight && markBox.size === 28, `the mark is a chip in the title's own row, at its end: ${JSON.stringify(markBox)}`);
  // a grade Canvas has not released carries no number at all — an unposted 0 reads like a real one
  await page.goto(`${BASE}/courses/101/assignments/1006`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  const heldChip = await page.$eval('.bcv-detail__grade', (e) => ({ held: e.classList.contains('bcv-detail__grade--held'), text: e.innerText.replace(/\s+/g, ' ').trim(), digits: /\d/.test(e.innerText) }));
  check(heldChip.held && /^Not yet posted/.test(heldChip.text) && !heldChip.digits, `a held grade says so and shows no number: ${JSON.stringify(heldChip)}`);
  // The mark opens the feedback screen — the same shape as a quiz's, in the course's own column.
  // The sheet it replaced is kept in the build, off behind SUB_SHEET, because the preview it framed
  // is Canvas's own document service and that service answers "service unavailable" often enough
  // that a sheet built around it reads as broken.
  await page.goto(`${BASE}/courses/104/assignments/4001`);
  await page.waitForSelector('.bcv-detail__grade', { timeout: 10000 });
  await page.click('.bcv-detail__grade');
  await page.waitForSelector('.bcv-fb__scorecard', { timeout: 10000 });
  const asScreen = await page.evaluate(() => ({ embedded: !!document.querySelector('.bcv-fb')?.closest('.bcv-qz.is-embedded'), sheet: !!document.querySelector('.bcv-sheet--sub') }));
  check(page.url().includes('bcv=feedback') && asScreen.embedded && !asScreen.sheet, `the mark opens a screen, not a sheet: ${page.url()} ${JSON.stringify(asScreen)}`);
  const sheetKept = await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    const [{ result }] = await chrome.scripting.executeScript({
      target: { tabId: tab.id }, world: 'ISOLATED', func: () => typeof self.BCV.screens.courseDetail.openSubmissions === 'function',
    });
    return result;
  }, BASE);
  check(sheetKept, 'and the sheet it replaced is still in the build, off rather than gone');
  const fbTop = await page.evaluate(() => ({
    score: document.querySelector('.bcv-fb__big').textContent,
    pct: document.querySelector('.bcv-fb__pct')?.textContent,
    summary: document.querySelector('.bcv-fb__summary').textContent,
    bar: document.querySelector('.bcv-fb__fill').style.width,
    comments: [...document.querySelectorAll('.bcv-fb__scorecard .bcv-fb__ctext')].map((e) => e.textContent),
  }));
  check(fbTop.score === '10 / 10' && fbTop.pct === '100%' && fbTop.bar === '100%' && /^Week 1 reflection · graded /.test(fbTop.summary), `it heads with the score, the percent and when it was graded: ${JSON.stringify(fbTop).slice(0, 170)}`);
  check(fbTop.comments.length === 1 && /derivative questions/.test(fbTop.comments[0]), `and the instructor's own words on the graded attempt: ${fbTop.comments.join(' | ').slice(0, 80)}`);
  // one card per attempt, latest first, each with its own facts, its own file and its own thread
  const subCards = await page.$$eval('.bcv-fb__q', (els) => els.map((e) => ({
    n: e.querySelector('.bcv-fb__qn').textContent,
    latest: !!e.querySelector('.bcv-fb__tag'),
    score: e.querySelector('.bcv-fb__score').textContent,
    facts: [...e.querySelectorAll('.bcv-fb__fact')].map((f) => `${f.querySelector('.bcv-fb__factk').textContent}=${f.querySelector('.bcv-fb__factv').textContent}`).join(' | '),
    files: [...e.querySelectorAll('.bcv-fb__file')].map((f) => f.innerText.replace(/\s+/g, ' ').trim()),
    thread: [...e.querySelectorAll('.bcv-fb__ctext')].map((c) => c.textContent),
    note: e.querySelector('.bcv-fb__note')?.textContent || null,
  })));
  check(subCards.length === 2 && subCards[0].n === 'Attempt 2' && subCards[0].latest && subCards[1].n === 'Attempt 1' && !subCards[1].latest, `one card per attempt, latest first: ${subCards.map((x) => `${x.n}${x.latest ? '*' : ''}`).join(' | ')}`);
  // the mock dates everything from today, so the expected strings come from its own answer, formatted the way the app formats a time
  const fmtAtLike = (iso) => { const d = new Date(iso); const h = d.getHours() % 12 || 12; return `${['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][d.getMonth()]} ${d.getDate()} at ${h}:${String(d.getMinutes()).padStart(2, '0')}${d.getHours() < 12 ? 'am' : 'pm'}`; };
  const sub4001 = await readSub('104', '4001');
  const latestHist = sub4001.submission_history.find((x) => Number(x.attempt) === 2);
  check(subCards[0].score === '10 / 10' && subCards[0].facts === `Submitted=${fmtAtLike(latestHist.submitted_at)} | Type=File upload | Graded=${fmtAtLike(sub4001.graded_at)}`, `the latest attempt's own facts, from Canvas's own dates: ${subCards[0].facts}`);
  check(subCards[1].score === 'Not graded' && /Type=Text entry/.test(subCards[1].facts) && /Only your latest attempt is graded/.test(subCards[1].note || ''), `an older attempt is not the graded one, and says so: ${subCards[1].score} · ${subCards[1].facts}`);
  // comments are filed against an attempt: a first draft's feedback is not feedback on the final one
  check(subCards[0].thread.length === 2 && /derivative questions/.test(subCards[0].thread[0]) && /Thanks, I see it now/.test(subCards[0].thread[1]), `the latest attempt's thread: ${subCards[0].thread.join(' | ').slice(0, 80)}`);
  check(subCards[1].thread.length === 1 && /only a first pass/.test(subCards[1].thread[0]), `and the first attempt's own: ${subCards[1].thread.join(' | ').slice(0, 60)}`);
  // the file is offered two ways on purpose: Canvas's preview service can be down, a download cannot
  check(subCards[0].files.length === 1 && /^PDF /.test(subCards[0].files[0]) && /\.pdf/.test(subCards[0].files[0]) && /KB/.test(subCards[0].files[0]) && /Preview Download$/.test(subCards[0].files[0]), `the file says its kind, name and size, and offers both ways at it: ${subCards[0].files[0]}`);
  check(await page.$eval('.bcv-fb__fileact--dl', (e) => /^\/files\/\w+\/download/.test(e.getAttribute('href') || '')), "the download is the file's own address, with no course context");
  // what was handed in is the student's own file, not the course's: asked for under /courses/:id it
  // is Canvas's 404 page, so it has to be previewed with no context at all
  await page.click('.bcv-fb__file .bcv-fb__fileact');
  await page.waitForSelector('.bcv-viewer-ov .bcv-viewer__frame', { timeout: 8000 });
  const attSrc = await page.$eval('.bcv-viewer-ov .bcv-viewer__frame', (e) => e.getAttribute('src'));
  check(/^\/files\/\w+\/file_preview/.test(attSrc || ''), `a submission attachment previews with no course context: ${attSrc}`);
  const attFramed = await eventually(async () => {
    const fr = page.frames().find((f) => /\/file_preview/.test(f.url()));
    return fr ? (await fr.$('#file_preview[data-bcv-scope="no-context"]')) !== null : false;
  });
  check(attFramed, 'and Canvas serves it, rather than the page-not-found it answers a course context with');
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-viewer-ov'), null, { timeout: 5000 });
  check(!!(await page.$('.bcv-fb__scorecard')), 'closing the file leaves the feedback screen it was opened from standing');
  // a reply of your own goes to Canvas, pinned to the attempt it was written on, and comes back on it
  const before = (await texts('.bcv-fb__q .bcv-fb__ctext')).length;
  await page.fill('.bcv-fb__input', 'Could you say more about part b?');
  await page.click('.bcv-fb__send');
  check(await eventually(async () => (await texts('.bcv-fb__ctext')).some((t) => /Could you say more about part b\?/.test(t))), 'a comment of your own is sent to Canvas and comes back on the thread');
  const posted = (await readSub('104', '4001')).submission_comments.find((cm) => /part b\?/.test(cm.comment));
  check((await texts('.bcv-fb__q .bcv-fb__ctext')).length === before + 1 && posted && Number(posted.attempt) === 2, `and Canvas has it against the attempt it was written on: ${JSON.stringify(posted && { attempt: posted.attempt })}`);
  check(/cannot be edited or deleted/.test((await texts('.bcv-fb__perm'))[0] || ''), 'the field says a comment cannot be taken back');
  // the way back out, as a quiz's feedback has: the rubric, the assignment, the course
  const fbBtns = await texts('.bcv-fb__btns .bcv-qz__big');
  check(fbBtns.join(' | ') === 'See the rubric | Back to the assignment | Back to F26-SPRK 010 103', `the way back out: ${fbBtns.join(' | ')}`);
  await page.click('.bcv-fb__btns .bcv-qz__big:nth-child(2)');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(!page.url().includes('bcv=feedback'), 'and Back to the assignment leaves the feedback screen behind');
  // ---- Mark as done, and Previous / Next ------------------------------------------------------
  // Mark as done is offered only where Canvas asks for it: a module item with a must_mark_done
  // requirement. The button is the requirement's own state, and pressing it makes Canvas's call.
  const seq = (cid, aid) => fetch(`${BASE}/api/v1/courses/${cid}/module_item_sequence?asset_type=Assignment&asset_id=${aid}`).then((r) => r.text()).then((x) => JSON.parse(x.replace(/^while\(1\);/, '')));
  check(!(await page.$('.bcv-detail__actions [aria-pressed]')), 'an assignment no module asks a mark for has no Mark as done');
  await page.goto(`${BASE}/courses/101/assignments/1003`);
  await page.waitForSelector('.bcv-detail__actions [aria-pressed]', { timeout: 10000 });
  const doneBtn = await page.$eval('.bcv-detail__actions [aria-pressed]', (e) => ({ text: e.textContent.trim(), pressed: e.getAttribute('aria-pressed'), done: e.classList.contains('is-done') }));
  check(doneBtn.text === 'Mark as done' && doneBtn.pressed === 'false' && !doneBtn.done && (await seq('101', '1003')).items[0].current.completion_requirement.completed === false, `a must_mark_done assignment offers Mark as done, unmarked: ${JSON.stringify(doneBtn)}`);
  await page.click('.bcv-detail__actions [aria-pressed]');
  check(await eventually(async () => (await page.$eval('.bcv-detail__actions [aria-pressed]', (e) => e.getAttribute('aria-pressed'))) === 'true'), 'pressing it marks the item done');
  const afterDone = await page.$eval('.bcv-detail__actions [aria-pressed]', (e) => ({ text: e.textContent.trim(), done: e.classList.contains('is-done') }));
  check(afterDone.text === 'Done' && afterDone.done && (await seq('101', '1003')).items[0].current.completion_requirement.completed === true, `the button says Done, green, and Canvas has the mark: ${JSON.stringify(afterDone)}`);
  await page.click('.bcv-detail__actions [aria-pressed]');
  // the button repaints once Canvas has answered and the caches are cleared: wait for it, not for the mock
  check(await eventually(async () => (await page.$eval('.bcv-detail__actions [aria-pressed]', (e) => e.textContent.trim())) === 'Mark as done') && (await seq('101', '1003')).items[0].current.completion_requirement.completed === false, 'pressing Done takes the mark back');
  // A live Canvas can answer the sequence with the item bare of its requirement, or name no item at
  // all for an assignment that sits in its module as a discussion or a quiz: the modules themselves
  // say then, and the button is there all the same.
  const modulesOf = (cid) => fetch(`${BASE}/api/v1/courses/${cid}/modules?include[]=items`).then((r) => r.text()).then((x) => JSON.parse(x.replace(/^while\(1\);/, '')));
  await fetch(`${BASE}/__mock/config`, { method: 'POST', body: JSON.stringify({ bareSequence: true }) });
  await page.goto(`${BASE}/courses/101/assignments/1003`);
  await page.waitForSelector('.bcv-detail__actions [aria-pressed]', { timeout: 10000 });
  check((await seq('101', '1003')).items[0].current.completion_requirement === undefined && (await page.$eval('.bcv-detail__actions [aria-pressed]', (e) => e.textContent.trim())) === 'Mark as done', 'the sequence without its requirement: the modules say, and Mark as done is there');
  await fetch(`${BASE}/__mock/config`, { method: 'POST', body: JSON.stringify({ bareSequence: false }) });
  await page.goto(`${BASE}/courses/105/assignments/5003`);
  await page.waitForSelector('.bcv-detail__actions [aria-pressed]', { timeout: 10000 });
  check((await seq('105', '5003')).items.length === 0 && (await page.$eval('.bcv-detail__actions [aria-pressed]', (e) => e.textContent.trim())) === 'Mark as done', 'a graded discussion sits in its module as the topic: the sequence names nothing, the modules do, and Mark as done is there');
  await page.click('.bcv-detail__actions [aria-pressed]');
  check(await eventually(async () => (await page.$eval('.bcv-detail__actions [aria-pressed]', (e) => e.getAttribute('aria-pressed'))) === 'true') && (await modulesOf('105'))[0].items[0].completion_requirement.completed === true, 'and pressing it marks the topic\'s item done in Canvas');
  await page.click('.bcv-detail__actions [aria-pressed]');
  check(await eventually(async () => (await page.$eval('.bcv-detail__actions [aria-pressed]', (e) => e.textContent.trim())) === 'Mark as done') && (await modulesOf('105'))[0].items[0].completion_requirement.completed === false, 'and takes it back');
  await page.goto(`${BASE}/courses/101/assignments/1003`); // where the checks below carry on
  await page.waitForSelector('.bcv-detail__actions [aria-pressed]', { timeout: 10000 });
  // Previous / Next go through the assignments in the order the Assignments tab lists them
  const groups = await fetch(`${BASE}/api/v1/courses/101/assignment_groups?include[]=assignments`).then((r) => r.text()).then((x) => JSON.parse(x.replace(/^while\(1\);/, '')));
  const ordered = groups.flatMap((g) => g.assignments || []).filter((a) => a.published !== false).sort((x, y) => ((Date.parse(x.due_at) || Infinity) - (Date.parse(y.due_at) || Infinity)) || String(x.name).localeCompare(String(y.name)));
  const at1003 = ordered.findIndex((a) => String(a.id) === '1003');
  const navBtns = await page.$$eval('.bcv-detail__nav .bcv-detail__navbtn', (els) => els.map((e) => ({ dir: e.classList.contains('bcv-detail__navbtn--prev') ? 'prev' : 'next', kicker: e.querySelector('.bcv-detail__navkicker').textContent, name: e.querySelector('.bcv-detail__navname').textContent })));
  const want = [ordered[at1003 - 1] && { dir: 'prev', kicker: 'Previous', name: ordered[at1003 - 1].name }, ordered[at1003 + 1] && { dir: 'next', kicker: 'Next', name: ordered[at1003 + 1].name }].filter(Boolean);
  check(JSON.stringify(navBtns) === JSON.stringify(want), `Previous and Next name the assignments either side, in the tab's order: ${navBtns.map((b) => `${b.kicker}: ${b.name}`).join(' | ')}`);
  await page.click('.bcv-detail__nav .bcv-detail__navbtn--next');
  // the old title is still on screen while the next page comes: wait for the new one, not for a title
  const nextOpened = await page.waitForFunction((x) => location.pathname.endsWith(`/assignments/${x.id}`) && document.querySelector('.bcv-detail__title')?.textContent === x.name, ordered[at1003 + 1], { timeout: 10000 }).then(() => true).catch(() => false);
  check(nextOpened, `Next opens the next assignment: ${(await texts('.bcv-detail__title'))[0]}`);
  await page.goto(`${BASE}/courses/104/assignments/4001`); // the rubric checks below read this page
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(!(await page.$('.bcv-rubg')) && (await texts('.bcv-detail__actions .bcv-rubbtn'))[0] === 'Rubric', 'the rubric keeps its own button, and is not on the page until it is asked for');
  await page.click('.bcv-detail__actions .bcv-rubbtn');
  await page.waitForSelector('.bcv-sheet--rub .bcv-rubg__row', { timeout: 8000 });
  // once a rubric is marked, the rating the work was given is the one filled in, with its score and note
  const marked = await page.$eval('.bcv-sheet--rub .bcv-rubg__row', (e) => ({ got: e.querySelector('.bcv-rubg__cell.is-got')?.innerText.replace(/\s+/g, ' ').trim(), pts: e.querySelector('.bcv-rubg__ptsv')?.textContent, note: e.querySelector('.bcv-rubg__note')?.textContent, cells: e.querySelectorAll('.bcv-rubg__cell').length }));
  check(/^3 Partial/.test(marked.got || '') && marked.pts === '4 / 6' && marked.cells === 3 && /Sign error in part b\./.test(marked.note || ''), `a marked criterion rings the level it was given, and carries its score and the marker's note: ${JSON.stringify(marked)}`);
  const rubHead = { desc: await page.$eval('.bcv-sheet--rub .bcv-sheet__desc', (e) => e.textContent), badge: await page.$eval('.bcv-rubg__badge', (e) => e.innerText.replace(/\s+/g, ' ').trim()) };
  check(rubHead.desc === 'Marked 8 / 10 · 2 criteria' && rubHead.badge === '10 PTS', `the sheet heads with the rubric's own total and what it gave: ${JSON.stringify(rubHead)}`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet--rub'), null, { timeout: 5000 });
  await page.goto(`${BASE}/courses/104/assignments/4002`);
  await page.waitForSelector('.bcv-detail__actions .bcv-btn--primary', { timeout: 10000 });
  check((await texts('.bcv-detail__actions .bcv-btn--primary'))[0] === 'Submit assignment', 'the assignment page offers our own submit flow');
  // the colours the page sets on its own text are its own in the light appearance: nothing is flipped
  check((await page.$eval('.bcv-prose span[style*="color"]', (e) => getComputedStyle(e).color)) === 'rgb(45, 59, 69)', 'the light appearance leaves the page\'s own text colours exactly as Canvas set them');
  // a link to a file in the assignment's own text opens the viewer over the page, not a new tab or Canvas's file page
  const proseTabs = [];
  const onProseTab = (p) => proseTabs.push(p);
  context.on('page', onProseTab);
  await page.click('.bcv-prose a.instructure_file_link');
  await page.waitForSelector('.bcv-viewer .bcv-viewer__frame', { timeout: 8000 });
  check(/Course Syllabus\.pdf/.test((await texts('.bcv-viewer .bcv-sheet__title'))[0]) && (await page.$eval('.bcv-viewer__frame', (e) => e.getAttribute('src'))) === '/courses/101/files/f1/file_preview' && proseTabs.length === 0 && page.url() === `${BASE}/courses/104/assignments/4002`, 'a file linked from an assignment\'s text opens in the viewer, over the assignment, with no new tab');
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-viewer'), null, { timeout: 3000 });
  context.off('page', onProseTab);
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
  check((await texts('.bcv-detail__title'))[0] === 'Dis01' && (await texts('.bcv-detail__meta'))[0].includes('Points 10'), 'assignment detail loads');
  // the rubric is a popup opened from the button beside Submit assignment, not a card down the page
  check((await texts('.bcv-detail__actions .bcv-rubbtn'))[0] === 'Rubric' && !(await page.$('.bcv-rubg')), 'the rubric is a press away from Submit assignment, not spent on the page');
  await page.click('.bcv-detail__actions .bcv-rubbtn');
  await page.waitForSelector('.bcv-sheet--rub .bcv-rubg__row', { timeout: 8000 });
  check((await page.$$('.bcv-sheet--rub .bcv-rubg__row')).length === 2, 'the rubric opens as a grid, one row per criterion');
  // the levels are columns, headed once, and coloured by what they are worth rather than by position
  const head = await page.$$eval('.bcv-rubg__head .bcv-rubg__h', (els) => els.map((e) => `${e.textContent}:${(e.className.match(/--(\w+)/) || [, 'plain'])[1]}`));
  check(head.join(' | ') === 'Criterion:plain | Full marks:full | Partial:part | No marks:none', `the levels head the columns once, best to nothing: ${head.join(' | ')}`);
  const cells = await page.$eval('.bcv-rubg__row', (row) => [...row.querySelectorAll('.bcv-rubg__cell')].map((e) => `${e.querySelector('.bcv-rubg__cellpts').textContent}/${e.querySelector('.bcv-rubg__celltext').textContent}/${(e.className.match(/cell--(\w+)/) || [, '?'])[1]}`));
  check(cells.join(' | ') === '6/Full marks/full | 3/Partial/part | 0/No marks/none' && !(await page.$('.bcv-rubg__cell.is-got')), `every level is a cell with what it is worth, and none is marked on an ungraded assignment: ${cells.join(' | ')}`);
  // a criterion is Canvas rich text: its own bullets and line breaks are drawn, not printed as markup
  const crit = await page.$eval('.bcv-sheet--rub .bcv-rubg__row', (e) => ({ long: e.querySelector('.bcv-rubg__desc')?.innerText || '', brs: e.querySelectorAll('.bcv-rubg__desc br').length, raw: e.textContent }));
  check(crit.brs === 2 && /Every answer is correct/.test(crit.long) && /Units on each one/.test(crit.long) && !/&lt;|<br/.test(crit.raw), `a criterion written as rich text reads as written, its markup never as text: ${JSON.stringify(crit)}`);
  // and one too long for its card is clamped, with a button that appears only where it is needed
  const clamp = await page.evaluate(() => {
    const rows = [...document.querySelectorAll('.bcv-rubg__row')];
    const at = (i) => { const d = rows[i].querySelector('.bcv-rubg__desc'); const b = rows[i].querySelector('.bcv-rubg__more'); return { cut: d.scrollHeight > d.clientHeight + 1, btn: !!b && !b.hidden }; };
    return { long: at(0), short: at(1) };
  });
  check(clamp.long.cut && clamp.long.btn && !clamp.short.cut && !clamp.short.btn, `a long criterion is clamped and offers More; a short one is left alone: ${JSON.stringify(clamp)}`);
  await page.click('.bcv-rubg__more');
  const opened = await page.$eval('.bcv-rubg__row', (e) => ({ open: e.querySelector('.bcv-rubg__desc').classList.contains('is-open'), label: e.querySelector('.bcv-rubg__more').textContent, shows: e.querySelector('.bcv-rubg__desc').scrollHeight <= e.querySelector('.bcv-rubg__desc').clientHeight + 1 }));
  check(opened.open && opened.label === 'Less' && opened.shows, `More shows the rest of it and becomes Less: ${JSON.stringify(opened)}`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet--rub'), null, { timeout: 5000 });
  check((await texts('.bcv-btn--primary'))[0] === 'Submit assignment', 'submit button opens our own submission flow');
  await shot(page, '14-assignment');

  // discussions
  await tab('discussions');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  const drows = await texts('.bcv-body .bcv-row');
  // 7003 was opened from the dashboard stream earlier in this run, so Canvas now reports it read
  check(drows.length === 4 && /Is there any discussion happening this week\?.*Last post.*23 replies/.test(drows[1]) && !/23 unread/.test(drows[1]) && drows.some((t) => /Discussion Quiz for this week.*1 unread/.test(t)), `discussions (read state from Canvas): ${drows[1].slice(0, 80)}`);
  await shot(page, '15-course-discussions');
  // starting one is Canvas's own editor, with everything Canvas offers there; our shell stays over it
  await page.click('.bcv-head__tools .bcv-btn--primary');
  await page.waitForSelector('html.bcv-punch #edit_discussion_form', { timeout: 15000 });
  check(page.url() === `${BASE}/courses/101/discussion_topics/new` && (await page.$('#bcv-app .bcv-rail')) !== null && /Topic Title/.test(await page.$eval('#edit_discussion_form', (e) => e.textContent)), `New discussion opens Canvas's own editor with the shell over it: ${page.url()}`);
  await page.goBack();
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 15000 });
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
  // a grade is the start of a question, and the answer is on the assignment's own page: the whole
  // row goes there, while the score keeps its own presses so a what-if can still be started on it
  const gradeRow = await page.$eval('.bcv-grades__main .bcv-card--list .bcv-row', (e) => ({ tag: e.tagName, href: e.getAttribute('href'), name: e.querySelector('.bcv-grade__name')?.textContent, score: e.querySelector('.bcv-grade__score')?.textContent }));
  check(gradeRow.tag === 'A' && /\/courses\/101\/assignments\/\d+$/.test(gradeRow.href || ''), `a grades row opens the assignment it is about: ${JSON.stringify(gradeRow)}`);
  await page.click('.bcv-grades__main .bcv-card--list .bcv-row .bcv-grade__score');
  await new Promise((r) => setTimeout(r, 400));
  check(/\/courses\/101\/grades$/.test(page.url()), 'and a press on the score stays put, so a what-if can be started there');
  await page.click('.bcv-grades__main .bcv-card--list .bcv-row');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(/\/courses\/101\/assignments\/\d+$/.test(page.url()) && (await texts('.bcv-detail__title'))[0] === gradeRow.name, `pressing the row lands on that assignment: ${page.url()}`);
  // and the mark is the first thing on it, not something to go looking for in the side column
  const top = await page.evaluate(() => {
    const g = document.querySelector('.bcv-detail__grade');
    const body = document.querySelector('.bcv-prose');
    return g ? { score: g.querySelector('.bcv-detail__gradescore')?.textContent, of: g.querySelector('.bcv-detail__gradeof')?.textContent, pc: g.querySelector('.bcv-detail__gradepc')?.textContent, aboveText: !!body && g.compareDocumentPosition(body) === Node.DOCUMENT_POSITION_FOLLOWING } : null;
  });
  // the same numbers the row carried, as a percentage too, and above the assignment's own text
  const [rowEarned, rowPoss] = gradeRow.score.split(' / ');
  check(top && top.score === rowEarned && top.of === `/ ${rowPoss}` && top.pc === `${Math.round((Number(rowEarned) / Number(rowPoss)) * 100)}%` && top.aboveText, `the mark is at the top of the assignment, the same one the grades row showed: ${JSON.stringify(top)} from ${gradeRow.score}`);
  await page.goto(`${BASE}/courses/101/grades`);
  await page.waitForSelector('.bcv-rings__svg', { timeout: 10000 });

  // ---- a grade that lands from elsewhere is never shown stale -----------------------------------------
  // A tool (an LTI plugin marking work in its own frame) or a teacher posts a grade while this page
  // keeps what it read. The scores are asked for again once a tool's frame has been on screen, and a
  // grades screen never draws from an answer older than its freshness — no reload, no look switch.
  console.log('grades stay fresh');
  const mockScore = (body) => fetch(`${BASE}/__mock/score`, { method: 'POST', body: JSON.stringify(body) }).then((r) => r.ok);
  const scoreOf = (name) => page.$$eval('.bcv-grades__main .bcv-row', (els, n) => els.find((e) => e.querySelector('.bcv-grade__name')?.textContent === n)?.querySelector('.bcv-grade__score')?.textContent || null, name);
  // in-place hops and a knob, driven from inside the interface (the content script's own world)
  const appGo = (path) => sw.evaluate(async ({ base, path: p }) => { const [t] = await chrome.tabs.query({ url: `${base}/*` }); await chrome.scripting.executeScript({ target: { tabId: t.id }, world: 'ISOLATED', func: (href) => { self.BCV.app.go(href); }, args: [p] }); }, { base: BASE, path });
  const setFreshness = (ms) => sw.evaluate(async ({ base, ms: v }) => { const [t] = await chrome.tabs.query({ url: `${base}/*` }); await chrome.scripting.executeScript({ target: { tabId: t.id }, world: 'ISOLATED', func: (n) => { self.BCV.store.freshness.grades = n; }, args: [v] }); }, { base: BASE, ms });
  await page.evaluate(() => { window.__bcvMark = 1; }); // only a reload clears it: everything below stays in place
  check((await scoreOf('Lec01-PreQuiz')) === '13 / 16', `the quiz's mark to begin with: ${await scoreOf('Lec01-PreQuiz')}`);
  check(await mockScore({ assignmentId: '1001', score: 15 }), 'a new mark lands in Canvas for it, behind the page\'s back');
  // a tool's frame on screen in between (Box, in the submit block of another course's assignment), then back to Grades, all in place
  await appGo('/courses/104/assignments/4002');
  await page.waitForSelector('.bcv-sb--embed', { timeout: 15000 });
  if (await page.$('.bcv-sb__done')) { await page.click('.bcv-sb__donebtns .bcv-sb__btn:not(.bcv-sb__btn--primary)'); await page.waitForSelector('.bcv-sb__tabs', { timeout: 5000 }); }
  await page.click('.bcv-sb__tab[data-tab=other]');
  await page.click('.bcv-sb__tool');
  await page.waitForSelector('.bcv-sheet--tool .bcv-sb__frame', { timeout: 5000 });
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet--tool'), null, { timeout: 5000 });
  const leaveAnyway = (d) => d.accept(); // an unsent draft in the block asks before leaving: leave
  page.on('dialog', leaveAnyway);
  await appGo('/courses/101/grades');
  await page.waitForSelector('.bcv-grades__main .bcv-row', { timeout: 15000 });
  page.off('dialog', leaveAnyway);
  check(await eventually(async () => (await scoreOf('Lec01-PreQuiz')) === '15 / 16') && (await page.evaluate(() => window.__bcvMark === 1)), `a tool's frame was on screen in between: back on Grades the new mark shows, asked for again, no reload (${await scoreOf('Lec01-PreQuiz')}, mark ${await page.evaluate(() => window.__bcvMark)})`);
  check(await mockScore({ assignmentId: '1001', score: 16 }), 'and another lands');
  await appGo('/');
  await page.waitForSelector('.bcv-stat', { timeout: 15000 });
  await appGo('/courses/101/grades');
  await page.waitForSelector('.bcv-grades__main .bcv-row', { timeout: 15000 });
  await page.waitForTimeout(500);
  check((await scoreOf('Lec01-PreQuiz')) === '15 / 16', `within the freshness the answer just read is drawn again, no request (${await scoreOf('Lec01-PreQuiz')})`);
  await setFreshness(1); // a millisecond: the suite cannot wait the 30 seconds (0 would mean no limit)
  await appGo('/');
  await page.waitForSelector('.bcv-stat', { timeout: 15000 });
  await appGo('/courses/101/grades');
  await page.waitForSelector('.bcv-grades__main .bcv-row', { timeout: 15000 });
  check(await eventually(async () => (await scoreOf('Lec01-PreQuiz')) === '16 / 16') && (await page.evaluate(() => window.__bcvMark === 1)), `past its freshness the mark is asked for again on the next look, no reload (${await scoreOf('Lec01-PreQuiz')}, mark ${await page.evaluate(() => window.__bcvMark)})`);
  await setFreshness(30000);
  // and never the browser's own cache: even where Canvas (or something in front of it) says an answer
  // may be kept for ten minutes, a fresh page asks again and sees the mark posted since
  await fetch(`${BASE}/__mock/config`, { method: 'POST', body: JSON.stringify({ cacheable: true }) });
  await page.goto(`${BASE}/courses/101/grades`);
  await page.waitForSelector('.bcv-grades__main .bcv-row', { timeout: 15000 });
  const lastApi = await fetch(`${BASE}/__mock/last-api`).then((r) => r.text()).then((t) => JSON.parse(t.replace(/^while\(1\);/, '')));
  check((await scoreOf('Lec01-PreQuiz')) === '16 / 16' && lastApi.headers['cache-control'] === 'no-cache' && lastApi.headers.pragma === 'no-cache', `every question to Canvas says not to answer from a cache (${lastApi.path}: ${lastApi.headers['cache-control']})`);
  await mockScore({ assignmentId: '1001', score: 14 });
  await page.goto(`${BASE}/courses/101/grades`);
  await page.waitForSelector('.bcv-grades__main .bcv-row', { timeout: 15000 });
  check(await eventually(async () => (await scoreOf('Lec01-PreQuiz')) === '14 / 16'), `a fresh page never draws a cached body: the mark posted since shows (${await scoreOf('Lec01-PreQuiz')})`);
  await fetch(`${BASE}/__mock/config`, { method: 'POST', body: JSON.stringify({ cacheable: false }) });
  await mockScore({ assignmentId: '1001', score: 13 }); // the seeded mark again for what follows
  await page.goto(`${BASE}/courses/101/grades`);
  await page.waitForSelector('.bcv-rings__svg', { timeout: 10000 });

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
  check(frows.length === 7 && /Course Information Folder · modified/.test(frows[0]) && frows.some((t) => /Course Syllabus.pdf PDF · modified .* 212 KB/.test(t)), `files: ${frows.find((t) => /Syllabus/.test(t))}`);
  check(!(await page.$('.bcv-btn--fill36')) && (await page.$$eval('.bcv-body .bcv-row[href*="/files/f"]', (els) => els.length)) === 4, 'no Download button in the header: each file is a link to its page that opens the viewer, which has its own');
  await shot(page, '20-course-files');
  // the file viewer: a file opens in a sheet over the page, never in a new tab
  const tabsOpened = [];
  const onTab = (p) => tabsOpened.push(p);
  context.on('page', onTab);
  await page.click('.bcv-body .bcv-row:has-text("Course Syllabus.pdf")');
  await page.waitForSelector('.bcv-viewer .bcv-viewer__frame', { timeout: 5000 });
  const vHead = (await texts('.bcv-viewer .bcv-sheet__head'))[0];
  check(/Course Syllabus\.pdf/.test(vHead) && /PDF · 212 KB · modified/.test(vHead) && (await page.$eval('.bcv-viewer__frame', (e) => e.getAttribute('src'))) === '/courses/101/files/f1/file_preview' && (await page.$eval('.bcv-viewer a[download]', (e) => e.getAttribute('href'))) === '/files/f1/download' && (await texts('.bcv-viewer__canvas'))[0] === 'Open in Canvas' && tabsOpened.length === 0 && page.url().endsWith('/courses/101/files'), `a PDF opens in the viewer over the page — Canvas's own preview framed, Download and Open in Canvas in the sheet — and no new tab: ${vHead}`);
  check((await page.$eval('.bcv-viewer__tab', (e) => [e.tagName, e.textContent.trim()].join(' | '))) === 'BUTTON | Open in new tab', 'an Open in new tab button is there for a PDF (a press hands a tab the file itself; Canvas\'s own address would download it)');
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-viewer'), null, { timeout: 3000 });
  check(await page.evaluate(() => document.activeElement?.classList.contains('bcv-row')), 'Escape closes it and hands focus back to the row');
  await page.click('.bcv-body .bcv-row:has-text("Lecture 3 whiteboard.png")');
  await page.waitForFunction(() => { const i = document.querySelector('.bcv-viewer__img'); return i && i.complete && i.naturalWidth > 0; }, null, { timeout: 5000 });
  check((await page.$eval('.bcv-viewer__img', (e) => e.naturalWidth)) === 640 && /Image · 295 KB/.test((await texts('.bcv-viewer .bcv-sheet__head'))[0]), 'an image is shown as itself');
  await shot(page, '20b-file-viewer');
  await page.click('.bcv-viewer .bcv-sheet__close');
  await page.waitForFunction(() => !document.querySelector('.bcv-viewer'), null, { timeout: 3000 });
  await page.click('.bcv-body .bcv-row:has-text("reading-list.txt")');
  await page.waitForFunction(() => /Reading list/.test(document.querySelector('.bcv-viewer__text')?.textContent || ''), null, { timeout: 5000 });
  check(/Chapter 3/.test((await texts('.bcv-viewer__text'))[0]), 'a text file shows its text');
  // Open in new tab, pressed: the tab is handed a copy of the file's own bytes (a blob address), and shows it
  const [openedTab] = await Promise.all([context.waitForEvent('page', { timeout: 8000 }), page.click('.bcv-viewer__tab')]);
  await openedTab.waitForFunction(() => location.protocol === 'blob:' && /Reading list/.test(document.body?.textContent || ''), null, { timeout: 8000 }).catch(() => {});
  check(openedTab.url().startsWith('blob:') && /Reading list/.test(await openedTab.evaluate(() => document.body.textContent)), `Open in new tab shows the file itself in a tab of its own: ${openedTab.url().slice(0, 30)}…`);
  await openedTab.close();
  await page.mouse.click(8, 8); // outside the sheet
  await page.waitForFunction(() => !document.querySelector('.bcv-viewer'), null, { timeout: 3000 });
  check(tabsOpened.length === 1, 'and only that press ever opened a tab');
  context.off('page', onTab);
  // a document a browser cannot show: the button is a link to Canvas's own page for the file
  await page.click('.bcv-body .bcv-row:has-text("planned lecture schedule.xlsx")');
  await page.waitForSelector('.bcv-viewer .bcv-viewer__frame', { timeout: 5000 });
  check((await page.$eval('.bcv-viewer__tab', (e) => [e.tagName, e.getAttribute('href'), e.getAttribute('target')].join(' | '))) === 'A | /courses/101/files/f2?bcv=native | _blank', 'for a document the browser cannot show, Open in new tab goes to Canvas\'s page for it');
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-viewer'), null, { timeout: 3000 });
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
  check((await page.$$('.bcv-module')).length === 1 && (await texts('.bcv-module__item')).length === 3, `modules list: ${(await texts('.bcv-module__item')).length} items (the page, the quiz, and the assignment the module asks a mark for)`);
  // Back means where you came from: the same page says Modules when opened from Modules, Pages when
  // opened from Pages, and Pages (the list it belongs to) when opened straight in with nothing before
  await clickScreen('.bcv-module__item[href*="/pages/course-information"]');
  await page.waitForSelector('.bcv-detail__back', { timeout: 10000 });
  check((await texts('.bcv-detail__back'))[0] === 'Modules' && page.url().endsWith('/courses/101/pages/course-information'), `a page opened from Modules says Back to Modules: ${(await texts('.bcv-detail__back'))[0]}`);
  await clickScreen('.bcv-detail__back');
  check(page.url().endsWith('/courses/101/modules') && (await page.$('.bcv-module')) !== null, 'and Back returns to Modules');
  await tab('pages');
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 10000 });
  await clickScreen('.bcv-body .bcv-row');
  await page.waitForSelector('.bcv-detail__back', { timeout: 10000 });
  check((await texts('.bcv-detail__back'))[0] === 'Pages' && page.url().endsWith('/courses/101/pages/course-information'), `the same page opened from Pages says Back to Pages: ${(await texts('.bcv-detail__back'))[0]}`);
  await page.evaluate(() => sessionStorage.removeItem('bcv:trail')); // straight in, nothing before it
  await page.goto(`${BASE}/courses/101/pages/course-information`);
  await page.waitForSelector('.bcv-detail__back', { timeout: 10000 });
  check((await texts('.bcv-detail__back'))[0] === 'Pages', 'a page opened straight in falls back to the list it belongs to');
  // and from outside a course: an assignment opened from To Do goes back to To Do, and the course
  // header's Back leaves the course for where it was entered from
  await nav('todo');
  await page.waitForSelector('.bcv-body .bcv-row a.bcv-row__body[href*="/assignments/"]', { timeout: 10000 });
  await clickScreen('.bcv-body .bcv-row a.bcv-row__body[href*="/assignments/"]');
  await page.waitForSelector('.bcv-detail__back', { timeout: 10000 });
  check((await texts('.bcv-detail__back'))[0] === 'To Do' && (await texts('.bcv-ctx__back'))[0] === 'To Do', `an assignment opened from To Do says Back to To Do, and so does the course header: ${(await texts('.bcv-detail__back'))[0]} / ${(await texts('.bcv-ctx__back'))[0]}`);
  await tab('modules');
  check((await texts('.bcv-ctx__back'))[0] === 'To Do', 'moving between the course\'s tabs keeps the header\'s Back on where the course was entered from');
  await clickScreen('.bcv-ctx__back');
  check(page.url().endsWith('/#todo') && (await page.$('.bcv-todo__add')) !== null, 'and it goes there');
  await page.goto(`${BASE}/courses/102/modules`);
  await page.waitForSelector('.bcv-module', { timeout: 10000 });
  const mods = await texts('.bcv-module__head');
  check(mods.length === 4 && /^Week 1: Kinematics .*2 of 2 requirements done$/.test(mods[0]) && /^Week 3: Energy .*Locked until/.test(mods[2]), `modules: ${mods.join(' | ')}`);
  // each head says when the module next wants something, so the list reads without opening anything
  // (the mock dates its modules from today: Week 1's report a week ago, Week 2's homework yesterday, Week 3 unlocking in five days, Week 0's quiz three weeks ago)
  const dayAt = (n) => { const d = new Date(); d.setDate(d.getDate() + n); return d; };
  const mdOf = (n) => { const d = dayAt(n); return `${['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][d.getMonth()]} ${d.getDate()}`; };
  const dowOf = (n) => ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'][dayAt(n).getDay()];
  check((await texts('.bcv-module__due')).join(' | ') === `Last due ${mdOf(-7)} | Last due ${mdOf(-1)} | Next due ${dowOf(5)} | Last due ${mdOf(-21)}` && (await page.$$('.bcv-module__due.is-next')).length === 1, `each module says when it next wants something: ${(await texts('.bcv-module__due')).join(' | ')}`);
  check((await page.$$('.bcv-circle.is-done')).length === 3 && (await page.$('.bcv-indent-1')), 'completion marks and indents');
  // by date is the order the work has to be done in, not the order the course was built in
  const names = () => texts('.bcv-module__name');
  check((await names()).join(' | ') === 'Week 1: Kinematics | Week 2: Forces | Week 3: Energy | Week 0: Orientation', `course order is Canvas's own: ${(await names()).join(' | ')}`);
  await page.click('.bcv-module__bar .bcv-seg__btn[data-value="date"]');
  await page.waitForFunction(() => document.querySelector('.bcv-module__name')?.textContent === 'Week 3: Energy', null, { timeout: 5000 });
  check((await names()).join(' | ') === 'Week 3: Energy | Week 2: Forces | Week 1: Kinematics | Week 0: Orientation', `Next due puts what is coming first, then what has gone by, most recent first: ${(await names()).join(' | ')}`);
  // open all / close all, and the slide rather than a jump
  const openCount = () => page.$$eval('.bcv-module', (els) => els.filter((e) => e.classList.contains('bcv-module--open')).length);
  check((await texts('.bcv-module__all'))[0] === 'Open all' && (await openCount()) < 4, 'not every module starts open, so the button offers Open all');
  await page.click('.bcv-module__all');
  check((await openCount()) === 4 && (await texts('.bcv-module__all'))[0] === 'Close all', 'Open all opens every module and turns into Close all');
  const slide = await page.$eval('.bcv-module--open .bcv-module__wrap', (e) => ({ prop: getComputedStyle(e).transitionProperty, rows: getComputedStyle(e).gridTemplateRows }));
  check(/grid-template-rows/.test(slide.prop) && slide.rows !== '0px', `opening and closing is a slide, not a jump: ${JSON.stringify(slide)}`);
  await page.click('.bcv-module__all');
  check((await openCount()) === 0 && (await texts('.bcv-module__all'))[0] === 'Open all', 'Close all closes every one');
  await page.click('.bcv-module__bar .bcv-seg__btn[data-value="course"]');
  await page.waitForFunction(() => document.querySelector('.bcv-module__name')?.textContent === 'Week 1: Kinematics', null, { timeout: 5000 });
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
  check(folded && /width/.test(await page.$eval('.bcv-side', (e) => getComputedStyle(e).transitionProperty)) && !(await page.$('#bcv-fab')), 'the attempt takes the page: the sidebar, header and rail fold away (a transition)');
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
  // A pick marks the option in place rather than drawing the question again: a formula in the
  // question or its options is an image Canvas serves, and rebuilding it made every equation on
  // screen blink away and back on each press.
  await page.$eval('#bcv-q2 .bcv-qz__qtext img.equation_image', (img) => { img.dataset.bcvSeen = '1'; });
  await page.click('#bcv-q2 .bcv-qz__opt:nth-child(2)');
  await page.click('#bcv-q2 .bcv-qz__opt:nth-child(2)');
  await waitText('.bcv-qz__answered', /4 of 4 answered · Saved/);
  const eqKept = await page.$eval('#bcv-q2 .bcv-qz__qtext img.equation_image', (img) => img.dataset.bcvSeen === '1');
  check(eqKept && (await page.$$('#bcv-q2 .bcv-qz__opt.is-selected')).length === 2 && (await page.$$('#bcv-q2 .bcv-qz__optlabel img.equation_image')).length === 2, 'a pick marks the option in place: the formulas on screen are never torn down and fetched again');
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
  check(/^Question 1 4 \/ 4 What is the velocity at t = 5\? You: -3\.15 m\/s Show all 5 options worked solution/i.test(fbCards[0]) && !/Correct:/.test(fbCards[0]) && (await page.$('.bcv-fb__q:nth-of-type(2) img.equation_image')), `a correct question: points, Explain, your answer, the instructor's solution with Canvas's equation image: ${fbCards[0]}`);
  check(/^Question 2 0 \/ 4 .*You: -2 m Correct: -3\.15 m Show all 5 options worked solution 17\.68 m is the position reading/i.test(fbCards[1]), `a wrong question shows the correct answer (show_correct_answers) and the incorrect-answer comment: ${fbCards[1]}`);
  check(/Question 3 4 \/ 4 .*Your instructor left no worked solution/i.test(fbCards[2]) && /Question 4 5 \/ 5 .*You: 3\.15 .*Only one root/i.test(fbCards[3]), `no solution says so; plain-text comments render too: ${fbCards[2]} | ${fbCards[3]}`);
  // an answer that is nothing but a formula: Canvas leaves its text empty and holds the equation as
  // an image, so the chip shows the equation rather than the blank it used to ("You: ,")
  const q3eq = await page.$$eval('.bcv-fb__q', (els) => {
    const chip = els[2].querySelector('.bcv-fb__chip');
    return { eqs: [...chip.querySelectorAll('img.equation_image')].map((e) => e.getAttribute('data-equation-content')), label: chip.firstChild.textContent };
  });
  check(q3eq.eqs.length === 2 && q3eq.eqs[0] === '\\vec{v}' && q3eq.label === 'You: ', `an answer that is only a formula shows the formula in the feedback: ${JSON.stringify(q3eq)}`);
  // the equation service sizes its picture in points at its own text size, so left alone a formula
  // towers over the sentence holding it; it is sized to that text instead, and sits on the line
  const eqFit = await page.$eval('.bcv-fb__solbody img.equation_image', (img) => {
    const cs = getComputedStyle(img);
    return { set: img.style.height, w: img.style.width, h: Math.round(img.getBoundingClientRect().height), fs: parseFloat(getComputedStyle(img.parentElement).fontSize), natural: img.naturalHeight, va: cs.verticalAlign, display: cs.display, attrs: img.hasAttribute('width') || img.hasAttribute('height') };
  });
  check(/em$/.test(eqFit.set) && eqFit.w === 'auto' && !eqFit.attrs && eqFit.h < eqFit.natural && eqFit.h > eqFit.fs && eqFit.h <= eqFit.fs * 4 && eqFit.va === 'middle' && eqFit.display === 'inline-block', `a formula is sized to the text it sits in and reads on the line: ${JSON.stringify(eqFit)}`);
  // every option the question offered, for checking the rest — folded away until asked for
  const optsBtn = await page.$$('.bcv-fb__q .bcv-fb__more');
  check(optsBtn.length === 3 && (await page.$eval('.bcv-fb__q .bcv-fb__more .bcv-fb__morelbl', (e) => e.textContent)) === 'Show all 5 options' && (await page.$eval('.bcv-fb__q .bcv-fb__opts', (e) => e.hidden)), 'each choice question offers its full list of options, folded away');
  await optsBtn[0].click();
  const shownOpts = await page.$$eval('.bcv-fb__q:nth-of-type(2) .bcv-fb__opt', (els) => els.map((e) => e.textContent.trim().replace(/\s+/g, ' ')));
  check(shownOpts.length === 5 && shownOpts[0] === 'A-3.15 m/sYour answerCorrect' && (await page.$eval('.bcv-fb__q .bcv-fb__more .bcv-fb__morelbl', (e) => e.textContent)) === 'Hide the options', `the options open with your pick marked: ${shownOpts.join(' | ')}`);
  // a wrong question marks the right one as well, since this quiz shows correct answers
  await (await page.$$('.bcv-fb__q .bcv-fb__more'))[1].click();
  const wrongOpts = await page.$$eval('.bcv-fb__q:nth-of-type(3) .bcv-fb__opt', (els) => els.map((e) => `${e.textContent.trim().replace(/\s+/g, ' ')}${e.classList.contains('is-right') ? ' [right]' : ''}${e.classList.contains('is-mine') ? ' [mine]' : ''}`));
  check(wrongOpts.length === 5 && wrongOpts.filter((t) => t.includes('[right]')).length === 1 && wrongOpts.filter((t) => t.includes('[mine]')).length === 1 && wrongOpts.some((t) => /Correct.*\[right\]/.test(t)), `a wrong question marks both your pick and the right one: ${wrongOpts.join(' | ')}`);
  await (await page.$$('.bcv-fb__q .bcv-fb__more'))[0].click();
  check(await page.$eval('.bcv-fb__q .bcv-fb__opts', (e) => e.hidden), 'pressing it again folds the options away');
  const eqText = await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    const [{ result }] = await chrome.scripting.executeScript({
      target: { tabId: tab.id }, world: 'ISOLATED',
      func: () => [
        self.BCV.utils.htmlToText('<p><img class="equation_image" title="\\vec{v}" alt="LaTeX: \\vec{v}" data-equation-content="\\vec{v}"></p>'),
        self.BCV.utils.htmlToText('<p>A diagram: <img src="/x.png" alt="the free-body diagram"></p>'),
      ],
    });
    return result;
  }, BASE);
  check(eqText[0] === '\\vec{v}' && eqText[1] === 'A diagram: the free-body diagram', `a formula Canvas keeps as an image reads as the formula, and any image as its alt text: ${JSON.stringify(eqText)}`);
  check((await page.$$eval('.bcv-fb > .bcv-enter', (els) => els.map((e) => e.style.getPropertyValue('--bcv-delay')))).join(',') === '0ms,45ms,90ms,135ms,180ms', 'feedback cards arrive on a 45ms stagger');
  await shot(page, '22h-quiz-feedback');
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
  // one question at a time + no going back. Canvas's API refuses to list these questions ("Cannot receive one
  // question at a time questions in the API"; the mock refuses too), so each one is read from Canvas's own quiz
  // page and every move goes through that page's record-answer form — which is also how Canvas enforces no
  // going back. Answers, flags, the clock and the submit stay API calls.
  // Out of the box a quiz Canvas locks is not taken here at all: it seals each answer as you pass it
  // and cannot be taken again, so it goes to Canvas's own page and the reason is on the screen.
  await page.goto(`${BASE}/courses/101/quizzes/9014`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check((await texts('.bcv-detail__actions .bcv-btn')).join(',') === 'Take it in Canvas,Open in Canvas' && (await texts('.bcv-detail')).join(' ').includes('seals each question once you leave it'), `a quiz that locks its questions is handed to Canvas, and says why: ${(await texts('.bcv-detail__actions'))[0]}`);
  await page.goto(`${BASE}/courses/101/quizzes/9014?bcv=take`);
  await page.waitForSelector('.bcv-qz__begin', { timeout: 10000 });
  check((await texts('.bcv-qz__begin'))[0] === 'Take it in Canvas' && (await texts('.bcv-qz__bullet')).some((t) => /seals each question once you leave it/.test(t)), 'and its intro sends you there too, rather than starting an attempt here');
  await page.click('.bcv-qz__begin');
  await page.waitForFunction(() => location.search.includes('bcv=native'), null, { timeout: 8000 });
  check(page.url().includes('bcv=native') && (await page.$('html.bcv-punch')) !== null, `taking it opens Canvas's own quiz page under our shell: ${page.url()}`);
  // The override says take it here anyway. Everything below runs with it on — which is also what
  // proves the setting reaches the screen.
  await setSettings({ quizzes: { lockedHere: true } });
  await page.goto(`${BASE}/courses/101/quizzes/9014`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check((await texts('.bcv-detail__actions .bcv-btn')).join(',') === 'Take the quiz,Open in Canvas' && !(await texts('.bcv-detail')).join(' ').includes('seals each question'), 'with the override on it has the same Take button as any other quiz, and the warning is gone');
  await page.click('.bcv-detail__actions .bcv-btn--primary');
  await page.waitForSelector('.bcv-qz__begin', { timeout: 10000 });
  check((await texts('.bcv-qz__bullet')).some((t) => /One question at a time, and you cannot go back/.test(t)) && (await texts('.bcv-qz__begin'))[0] === 'Begin attempt', 'the intro says one at a time and no going back; Begin starts the attempt here');
  // a slow quiz page: the popup opens at once with a pill per question and the first filling as the progress bar — no skeleton
  await noteApi('POST', '/__mock/config', { quizPageDelay: 900 });
  await page.click('.bcv-qz__begin');
  await page.waitForSelector('.bcv-qz__pill:first-child.is-loading', { timeout: 4000 });
  const fillBox = await page.$eval('.bcv-qz__pill.is-loading', (e) => { const cs = getComputedStyle(e); return { anim: cs.animationName, origin: cs.backgroundOrigin, clip: cs.backgroundClip }; });
  check((await page.$$('.bcv-qz__pill')).length === 4 && !(await page.$('.bcv-qz .bcv-skel')) && (await texts('.bcv-qz__starting'))[0] === 'Starting your attempt…' && fillBox.anim === 'bcv-pill-load' && fillBox.origin === 'border-box' && fillBox.clip === 'border-box', `the attempt opens at once: four pills for the questions to come, the first filling across the whole pill, no skeleton (${JSON.stringify(fillBox)})`);
  await page.waitForSelector('.bcv-qz__opt', { timeout: 10000 });
  check(!(await page.$('.bcv-qz__pill.is-loading')) && !(await page.$('.bcv-qz__body.is-busy')), 'the fill goes once the question is there');
  check((await page.$$('.bcv-qz__pill')).length === 4 && (await page.$('.bcv-qz__pill:first-child.is-current')) && (await texts('.bcv-qz__qnum'))[0] === 'Question 1' && (await texts('.bcv-qz__qof'))[0] === 'of 4 · 4 points' && (await texts('.bcv-qz__optlabel')).join('|') === '-3.15 m/s|-2 m/s|0 m/s|1.37 m/s|None of the above', `question 1 as read from Canvas's own quiz page, the list of four from its right column: ${(await texts('.bcv-qz__optlabel')).join(' | ')}`);
  check(!(await visible('.bcv-qz__modes')) && (await texts('.bcv-qz__foot .bcv-qz__btn')).join(',') === 'Next', 'no mode switch and no Back button when the quiz forbids it');
  await page.click('.bcv-qz__opt');
  await waitText('.bcv-qz__answered', /1 of 4 answered · Saved/);
  const oqSub = (await noteApi('GET', '/api/v1/courses/101/quizzes/9014/submissions')).quiz_submissions.find((s) => s.workflow_state === 'untaken');
  let oq = await noteApi('GET', `/__mock/quizsub/${oqSub.id}`);
  check(String(oq.answers['90141']) === '901411' && !oq.read['90141'], 'the pick went through the quiz-submission API; nothing is marked read yet');
  await page.click('.bcv-qz__flag');
  await page.waitForSelector('.bcv-qz__flag.is-on', { timeout: 5000 });
  await page.click('.bcv-qz__btn--next');
  await waitText('.bcv-qz__qnum', /Question 2/);
  oq = await noteApi('GET', `/__mock/quizsub/${oqSub.id}`);
  check(oq.read['90141'] === true && oq.flags['90141'] === true && (await page.$('.bcv-qz__pill:first-child:disabled')) && (await page.$('.bcv-qz__pill:first-child.is-answered.is-flagged')) && (await page.$('.bcv-qz__pill:nth-child(2).is-current')), 'Next posted Canvas\'s record-answer form: question 1 is marked read (and locked here), the flag went through the API, question 2 came from the next page');
  await page.click('.bcv-qz__exit');
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(page.url() === `${BASE}/courses/101/quizzes/9014` && (await texts('.bcv-detail__actions .bcv-btn--primary'))[0] === 'Resume attempt', 'Save and exit keeps the attempt open: the quiz page offers to resume it');
  await page.click('.bcv-detail__actions .bcv-btn--primary');
  await page.waitForSelector('.bcv-qz__begin', { timeout: 10000 });
  await page.click('.bcv-qz__begin');
  await page.waitForSelector('.bcv-qz__opt', { timeout: 10000 });
  check((await texts('.bcv-qz__qnum'))[0] === 'Question 2' && (await page.$('.bcv-qz__pill:first-child.is-answered:disabled')) && (await texts('.bcv-qz__answered'))[0].startsWith('1 of 4 answered'), 'resuming lands on the first unread question, as Canvas decides, with question 1 still counted as answered');
  await page.click('.bcv-qz__opt:nth-child(4)');
  await waitText('.bcv-qz__answered', /2 of 4 answered · Saved/);
  await page.click('.bcv-qz__btn--next');
  // while question 3 is on its way, question 2 stays on screen (inert) and pill 3 fills: the pill is the loading animation
  await page.waitForSelector('.bcv-qz__pill:nth-child(3).is-loading', { timeout: 4000 });
  const fillNow = await page.$eval('.bcv-qz__pill:nth-child(3)', (e) => { const cs = getComputedStyle(e); return { anim: cs.animationName, size: cs.backgroundSize, img: cs.backgroundImage.slice(0, 16) }; });
  check((await texts('.bcv-qz__qnum'))[0] === 'Question 2' && !!(await page.$('.bcv-qz__body.is-busy')) && !(await page.$('.bcv-qz .bcv-skel')) && fillNow.anim === 'bcv-pill-load' && fillNow.img === 'linear-gradient(', `moving on: the next question's pill fills like a sidebar row while the current one stays put — no skeleton (${JSON.stringify(fillNow)})`);
  await waitText('.bcv-qz__qnum', /Question 3/);
  await noteApi('POST', '/__mock/config', { quizPageDelay: 0 });
  await page.click('.bcv-qz__opt:nth-child(1)');
  await page.click('.bcv-qz__opt:nth-child(3)');
  await waitText('.bcv-qz__answered', /3 of 4 answered · Saved/);
  check((await page.$$('.bcv-qz__opt.is-selected')).length === 2, 'a multiple-answer question read from the page keeps every pick');
  await page.click('.bcv-qz__btn--next');
  await waitText('.bcv-qz__qnum', /Question 4/);
  await page.fill('.bcv-qz__q input', '3.15');
  await waitText('.bcv-qz__answered', /4 of 4 answered · Saved/);
  check((await texts('.bcv-qz__foot .bcv-qz__btn')).join(',') === 'Review answers', 'the last page offers the review');
  await page.click('.bcv-qz__foot .bcv-qz__btn--primary');
  await page.waitForSelector('.bcv-qz__sum', { timeout: 5000 });
  const oqSums = await texts('.bcv-qz__sum');
  check(oqSums.length === 4 && /^Q1.*Answered$/.test(oqSums[0]) && /^Q4.*3\.15$/.test(oqSums[3]) && (await page.$$('.bcv-qz__sum:disabled')).length === 4 && /4 of 4 answered · nothing left blank/.test((await texts('.bcv-qz__lead'))[0]), `review lists every question — the ones passed as Canvas reports them, none re-openable: ${oqSums.join(' | ')}`);
  page.once('dialog', (d) => d.accept());
  await page.click('.bcv-qz__big--primary');
  await page.waitForSelector('.bcv-qz__done', { timeout: 10000 });
  check((await texts('.bcv-qz__h1'))[0] === 'Attempt submitted' && /Score \d+ \/ 20/.test((await texts('.bcv-qz__donecard'))[1] || ''), `submitted through the API, with the score Canvas returned: ${(await texts('.bcv-qz__donecard')).join(' | ')}`);
  await page.click('.bcv-qz__donebtns .bcv-qz__big--primary');
  await page.waitForSelector('.bcv-fb__q', { timeout: 10000 });
  check((await page.$$('.bcv-fb__q')).length === 4 && (await texts('.bcv-fb__chip')).some((t) => t === 'You: -3.15 m/s') && (await texts('.bcv-fb__chip')).some((t) => t === 'You: 3.15'), `feedback for a one-at-a-time quiz: the attempt's own question set, the answers from the graded history: ${(await texts('.bcv-fb__chip')).join(' | ')}`);
  // a quiz graded before today (seeded): its attempt row opens the feedback, with the instructor's comment
  await page.goto(`${BASE}/courses/101/quizzes/9001`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check((await texts('.bcv-detail__actions .bcv-btn--primary'))[0] === 'See feedback' && (await texts('.bcv-detail__actions .bcv-badge'))[0] === 'No attempts left · 1 attempt allowed' && (await page.$eval('.bcv-col .bcv-row[href]', (a) => a.getAttribute('href'))) === '/courses/101/quizzes/9001?bcv=feedback&attempt=1', 'a used-up quiz leads to its feedback from the button and the attempt row');
  await page.click('.bcv-col .bcv-row[href]');
  await page.waitForSelector('.bcv-fb__q', { timeout: 10000 });
  const fbLine2 = (await texts('.bcv-fb__scoreline'))[0];
  const fbComments = await texts('.bcv-fb__ctext');
  check(/^13 \/ 16 81% 3 of 4 correct · graded /.test(fbLine2) && fbComments.length === 1 && /^Nice work on the derivative questions/.test(fbComments[0]) && (await texts('.bcv-fb__avatar'))[0] === 'YL' && (await texts('.bcv-fb__btns .bcv-qz__big')).join('|') === 'Back to F26-MATH 021 20', `feedback from the quiz page: the instructor's comment (never your own reply): ${fbLine2} · ${fbComments.join(' | ')}`);
  check(/Question 2 0 \/ 4 .*You: 17\.68 m Correct: -3\.15 m/i.test((await texts('.bcv-fb__q'))[1]), `the seeded wrong answer with the correct one beside it: ${(await texts('.bcv-fb__q'))[1]}`);
  await shot(page, '22j-quiz-feedback-seeded');

  // ---- matching, and the kinds with a blank each ---------------------------------------------------
  // Canvas draws these as dropdowns and used to be the only place that could take them; they are
  // answered here now, and go up in the shapes Canvas's own API asks for.
  console.log('quiz: matching and blanks');
  await noteApi('POST', '/__mock/config', { richQuestions: true });
  await page.goto(`${BASE}/courses/101/quizzes/9001`);
  await page.waitForSelector('.bcv-detail__actions .bcv-btn--primary', { timeout: 20000 });
  await page.click('.bcv-detail__actions .bcv-btn--primary'); // to the quiz's own intro
  await page.waitForSelector('.bcv-qz__begin', { timeout: 20000 });
  await page.click('.bcv-qz__begin');
  await page.waitForSelector('.bcv-qz__pill', { timeout: 20000 });
  check((await page.$$('.bcv-qz__pill')).length === 6, `the attempt carries every question, the two new kinds included: ${(await page.$$('.bcv-qz__pill')).length}`);
  // matching: a row per left-hand value, the same list of right-hand ones beside each
  await page.click('.bcv-qz__pill:nth-child(5)');
  await waitText('.bcv-qz__qnum', /Question 5/);
  const matchLeft = await texts('.bcv-qz__matchleft');
  await page.click('.bcv-qz__matchrow:first-child .bcv-qz__sel');
  await page.waitForSelector('.bcv-picker__list', { timeout: 5000 });
  const matchOpts = await page.$$eval('.bcv-picker__list .bcv-picker__opt', (els) => els.map((e) => e.textContent.trim()));
  await page.keyboard.press('Escape');
  check(matchLeft.length === 3 && matchLeft[0] === '9.8' && matchOpts.length === 4 && matchOpts[0] === 'Choose…' && matchOpts.includes('Speed of light') && !(await page.$('.bcv-qz__q .bcv-hint')), `a matching question is answered here: ${matchLeft.join(' | ')} → ${matchOpts.slice(1).join(' | ')}`);
  const pickIn = async (sel, label) => {
    await page.click(sel);
    await page.waitForSelector('.bcv-picker__list', { timeout: 5000 });
    await page.click(`.bcv-picker__list .bcv-picker__opt:has-text("${label}")`);
    await page.waitForFunction(() => !document.querySelector('.bcv-picker__list'), null, { timeout: 5000 });
  };
  await pickIn('.bcv-qz__matchrow:nth-child(1) .bcv-qz__sel', 'Acceleration due to gravity');
  await pickIn('.bcv-qz__matchrow:nth-child(2) .bcv-qz__sel', 'Speed of light');
  await pickIn('.bcv-qz__matchrow:nth-child(3) .bcv-qz__sel', 'Gravitational constant');
  await waitText('.bcv-qz__answered', /1 of 6 answered · Saved/);
  check(true, 'every pair saves as it is set');
  await shot(page, '22i-quiz-matching');
  // a blank each, from a dropdown of that blank's own list
  await page.click('.bcv-qz__pill:nth-child(6)');
  await waitText('.bcv-qz__qnum', /Question 6/);
  const blankLbls = await texts('.bcv-qz__blanklbl');
  check(blankLbls.join('|') === 'rate|what' && (await page.$$('.bcv-qz__blanks .bcv-picker')).length === 2, `a blank each, named for the blank it fills: ${blankLbls.join(' | ')}`);
  await pickIn('.bcv-qz__blankrow:nth-child(1) .bcv-qz__sel', 'rate of change');
  await pickIn('.bcv-qz__blankrow:nth-child(2) .bcv-qz__sel', 'position');
  await waitText('.bcv-qz__answered', /2 of 6 answered · Saved/);
  // Canvas kept them: its own take page comes back with the same picks set
  const kept = await sw.evaluate(async () => {
    const html = await (await fetch('http://localhost:8787/courses/101/quizzes/9001/take')).text();
    return (html.match(/<option value="[^"]*" selected>[^<]*<\/option>/g) || []).map((m) => m.replace(/.*selected>/, '').replace('</option>', ''));
  });
  check(kept.length === 5 && kept.join(' | ') === 'Acceleration due to gravity | Speed of light | Gravitational constant | rate of change | position', `Canvas's own page comes back with every pick set, in its own shapes: ${kept.join(' | ')}`);
  // and the review names what was set, rather than a bare id
  await page.click('.bcv-qz__foot .bcv-qz__btn--primary');
  await page.waitForSelector('.bcv-qz__sum', { timeout: 10000 });
  const sums6 = await texts('.bcv-qz__sum');
  check(/9\.8 → Acceleration due to gravity/.test(sums6[4]) && /rate: rate of change/.test(sums6[5]), `the review names both sides of every pair and every blank: ${sums6[4]} | ${sums6[5]}`);
  // ---- the way out of the quiz UI, and never a reload out from under one ---------------------------
  // The button is red because it is a way out: it hands the attempt to Canvas as it stands.
  const rawBtn = await page.$eval('.bcv-qz__raw', (e) => ({ text: e.textContent.trim(), title: e.title, red: (([, r, g, b]) => ({ r: +r, g: +g, b: +b }))(getComputedStyle(e).color.match(/(\d+), (\d+), (\d+)/)) }));
  check(/Canvas page/.test(rawBtn.text) && /answers are already saved/.test(rawBtn.title) && /not restarted/.test(rawBtn.title) && rawBtn.red.r > 150 && rawBtn.red.r > rawBtn.red.g * 1.8, `a way out of the quiz UI, and it says what it does not do: ${JSON.stringify(rawBtn)}`);
  check(!(await page.$('.bcv-qz__rawnote')) && (await page.$eval('.bcv-qz__head', (e) => !/nothing restarts/.test(e.textContent))), 'and the promise stays in the tooltip: no note under the pills');
  // the quiz's own instructions are on the intro card, which the attempt takes the page from — so
  // they stay a press away in the header, as the quiz wrote them
  check(await visible('.bcv-qz__instrbtn'), 'the instructions are reachable from the attempt');
  await page.click('.bcv-qz__instrbtn');
  await page.waitForSelector('.bcv-sheet--instr .bcv-qz__instr .bcv-prose', { timeout: 8000 });
  const instr = await page.$eval('.bcv-sheet--instr', (e) => e.textContent);
  check(/Instructions/.test(instr) && /four questions on the pre-lecture reading/.test(instr) && (await page.$('.bcv-qz__sum')), `the quiz's own instructions open over the attempt, which is still there underneath: ${instr.slice(0, 80)}`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet--instr'), null, { timeout: 5000 });
  // nothing reloads a quiz: a screen that gives way goes to Canvas's own quiz page instead
  const noReload = await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    const [{ result }] = await chrome.scripting.executeScript({
      target: { tabId: tab.id }, world: 'ISOLATED',
      func: () => {
        let reloaded = false;
        const real = self.location.reload;
        self.location.reload = () => { reloaded = true; };
        const ok = self.BCV.app.recover('test');
        self.location.reload = real;
        return { ok, reloaded, settled: document.documentElement.classList.contains('bcv-settled') };
      },
    });
    return result;
  }, BASE);
  check(noReload.ok === false && noReload.reloaded === false, `a quiz is never reloaded out from under: ${JSON.stringify(noReload)}`);
  check(await eventually(async () => /Reload the page to continue/.test((await texts('.bcv-toast')).join(' '))), 'it says so rather than doing it');
  // the button leaves for Canvas's own page, carrying the attempt
  await page.click('.bcv-qz__raw');
  await page.waitForFunction(() => /bcv=native/.test(location.search), null, { timeout: 15000 });
  await page.waitForLoadState('domcontentloaded');
  check(/\/courses\/101\/quizzes\/9001\/take\?bcv=native$/.test(page.url()), `the way out lands on Canvas's own take page, with the attempt still open: ${page.url()}`);

  // ---- every attempt is listed, and an earlier one can be opened -----------------------------------
  // Canvas keeps one quiz submission per student — the one in play — so the attempts before it come
  // from the assignment submission's history. The open one carries the best score so far, which is an
  // earlier attempt's: it must not be shown against the attempt still being taken.
  await page.goto(`${BASE}/courses/101/quizzes/9001`);
  await page.waitForSelector('.bcv-col .bcv-row[href]', { timeout: 20000 });
  const attempts = await page.$$eval('.bcv-col .bcv-row[href]', (els) => els.map((e) => ({ t: e.textContent.replace(/\s+/g, ' ').trim(), href: e.getAttribute('href') })));
  check(attempts.length === 2 && /^Attempt 1Finished.*13 \/ 16$/.test(attempts[0].t) && attempts[0].href === '/courses/101/quizzes/9001?bcv=feedback&attempt=1' && attempts[1].t === 'Attempt 2In progress—' && /bcv=take$/.test(attempts[1].href), `both attempts are listed, the one in play without a score that is not its own: ${attempts.map((x) => x.t).join(' | ')}`);
  check(/2 of 5 used · attempt 2 in progress/.test(await page.$eval('.bcv-detail__meta, .bcv-detail', (e) => e.textContent)), `the header counts the attempt being taken: ${(await page.$eval('.bcv-detail', (e) => e.textContent)).match(/\d+ of \d+ used[^·]*(· attempt \d+ in progress)?/)?.[0]}`);
  // and the earlier one opens, with its own score rather than the best so far
  await page.click('.bcv-col .bcv-row[href*="attempt=1"]');
  await page.waitForSelector('.bcv-fb__scoreline', { timeout: 15000 });
  check(/^13 \/ 16 /.test((await texts('.bcv-fb__scoreline'))[0]) && (await page.$$('.bcv-fb__q')).length >= 4, `an earlier attempt opens its own feedback: ${(await texts('.bcv-fb__scoreline'))[0]}`);
  await noteApi('POST', '/__mock/config', { richQuestions: false });
  await page.goto(`${BASE}/courses/101/quizzes/9001`);
  await page.waitForSelector('.bcv-qz__intro, .bcv-detail__title', { timeout: 20000 });

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

  // the interface never says "AI" anywhere
  await page.goto(`${BASE}/courses/101/assignments/1009`);
  await page.waitForSelector('.bcv-detail__title', { timeout: 10000 });
  check(!(await page.content()).match(/\bAI\b/), 'the interface never says "AI"');

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
  // a Canvas page that paints its own light panel keeps dark text there, so a school's template
  // does not read as blank in the dark appearance
  await page.goto(`${BASE}/courses/101/pages/course-information`);
  await page.waitForSelector('.bcv-prose', { timeout: 10000 });
  await page.waitForFunction(() => !!document.querySelector('.bcv-prose .bcv-onlight'), null, { timeout: 5000 }).catch(() => {});
  const onLight = await page.evaluate(() => {
    const panel = document.querySelector('.bcv-prose .bcv-onlight');
    const plain = document.querySelector('.bcv-prose > p');
    const lum = (c) => { const [r, g, b] = /rgba?\(([^)]+)\)/.exec(c)[1].split(',').map(Number); return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255; };
    return {
      marked: !!panel,
      panelInk: panel ? lum(getComputedStyle(panel.querySelector('h2') || panel).color) : null,
      panelBg: panel ? lum(getComputedStyle(panel).backgroundColor) : null,
      plainInk: plain ? lum(getComputedStyle(plain).color) : null,
    };
  });
  check(onLight.marked && onLight.panelBg > 0.55 && onLight.panelInk < 0.3 && onLight.plainInk > 0.7, `a light panel of the page's own keeps dark text in the dark appearance, while the rest of the page stays light on dark: ${JSON.stringify(onLight)}`);
  await shot(page, '27c-dark-page-onlight');
  // the other half of it: ink the page paints dark (Canvas's own editor writes #2D3B45 into a paste)
  // was written for a white page, so on ours it is turned over rather than left to sink into black
  await page.goto(`${BASE}/courses/104/assignments/4002`);
  await page.waitForSelector('.bcv-prose span[style*="color"]', { timeout: 10000 });
  await page.waitForFunction(() => {
    const e = document.querySelector('.bcv-prose span[style*="color"]');
    const [r, g, b] = /rgba?\(([^)]+)\)/.exec(getComputedStyle(e).color)[1].split(',').map(Number);
    return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255 > 0.6;
  }, null, { timeout: 5000 }).catch(() => {});
  const flipped = await page.$eval('.bcv-prose span[style*="color"]', (e) => {
    const rgb = (c) => /rgba?\(([^)]+)\)/.exec(c)[1].split(',').map(Number);
    const [r, g, b] = rgb(getComputedStyle(e).color);
    const [br, bg, bb] = rgb(getComputedStyle(document.querySelector('#bcv-app')).backgroundColor);
    return { r, g, b, lum: (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255, bgLum: (0.2126 * br + 0.7152 * bg + 0.0722 * bb) / 255 };
  });
  check(flipped.lum > 0.6 && flipped.lum - flipped.bgLum > 0.5 && flipped.b > flipped.r, `dark ink the page set is turned over in the dark appearance, hue and all, rather than left on black: ${JSON.stringify(flipped)}`);
  await shot(page, '27d-dark-page-flipped');
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

  // ---- the look off and on: the switch at the top right, "Open in stock Canvas", Persistent ------------
  console.log('the look off and on');
  const lookSaved = async () => (await sw.evaluate(async () => (await self.BCV.settings.get()).appearance)).skin !== false;
  const lookAt = () => page.$eval('#bcv-look .bcv-look__main', (e) => e.getAttribute('aria-checked')).catch(() => null);
  const stockShown = async () => { await page.waitForFunction(() => !document.documentElement.classList.contains('bcv-on') && !!document.querySelector('#bcv-look') && !!document.querySelector('#application'), null, { timeout: 15000 }); await page.waitForTimeout(300); };
  await page.goto(`${BASE}/courses/101/external_tools/9`);
  await page.waitForSelector('html.bcv-punch #content', { timeout: 10000 });
  // Persistent is off to begin with: "Open in stock Canvas" is for this page view only
  await page.click('.bcv-native__stock');
  await stockShown();
  check(await visible('#application') && !(await page.$('#bcv-app')) && (await lookAt()) === 'false' && (await lookSaved()) === true, '"Open in stock Canvas" shows stock Canvas with the switch at the top right reading off, and the saved look untouched');
  check(!(await page.$('html.bcv-punch')) && (await visible('#header')) && (await page.$eval('#content', (el) => el.getBoundingClientRect().left < 200)), 'turning the look off ends the punch-through: Canvas lays its page out itself again');
  await shot(page, '28-skin-off');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  check(await visible('#bcv-app') && (await lookAt()) === 'true', 'with Persistent off, the next page brings the saved look back');
  // the switch at the top right: stock Canvas for this page, and a reload brings the look back
  await page.click('#bcv-look .bcv-look__main');
  await stockShown();
  check(await visible('#application') && !(await page.$('#bcv-app')) && (await lookAt()) === 'false' && (await lookSaved()) === true, 'the switch at the top right shows stock Canvas for this page, the saved look untouched');
  await page.reload();
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  check(await visible('#bcv-app') && (await lookAt()) === 'true', 'and a reload brings the look back');
  // Persistent on, from the row under the switch: the same switch then saves the choice, and every page follows
  await page.hover('#bcv-look');
  await eventually(() => page.$eval('#bcv-look .bcv-look__persist', (e) => e.getBoundingClientRect().height > 20));
  await page.click('#bcv-look .bcv-look__persist');
  await eventually(async () => (await sw.evaluate(async () => (await self.BCV.settings.get()).appearance.persistLook)) === true);
  await page.waitForTimeout(400);
  check(await visible('#bcv-app') && (await lookAt()) === 'true', 'Persistent going on changes nothing on the page');
  await page.click('#bcv-look .bcv-look__main');
  await stockShown();
  check(await visible('#application') && (await lookSaved()) === false, 'with Persistent on, the switch at the top right saves the look off');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#application', { timeout: 10000 });
  await page.waitForTimeout(400);
  check(!(await page.$('#bcv-app')) && (await lookAt()) === 'false' && (await page.$eval('#bcv-look .bcv-look__persist', (e) => e.getAttribute('aria-checked'))) === 'true', 'the look stays off on the next page load, and the row over stock Canvas reads Persistent on');
  await page.click('#bcv-look .bcv-look__main');
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  check(await visible('#bcv-app') && (await lookSaved()) === true && (await lookAt()) === 'true', 'and the switch brings it back, saved');
  await setSettings({ appearance: { persistLook: false } }); // the default again for what follows
  await page.waitForTimeout(300);
  // ...and it comes back even when the copy that hears the change has no listener. Safari re-injects
  // a site's content scripts when the extension looks at its permissions (opening the toolbar popup
  // does): the re-injected app.js finds the interface already booted and stands down, so the fresh
  // early.js beside it has nobody to tell. The look button then flipped a class and nothing happened.
  await setSettings({ appearance: { skin: false } });
  await page.waitForFunction(() => !document.documentElement.classList.contains('bcv-on'), null, { timeout: 8000 });
  await page.evaluate(() => { window.__bcvMark = 1; }); // only a reload clears it
  await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    // early.js again, exactly as Safari puts it back: a fresh copy with no listener of its own
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', files: chrome.runtime.getManifest().content_scripts[0].js });
    // and app.js left believing the look is already on, so it will not act — whatever reloads now is the early script
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', func: () => { self.BCV.app.state.settings.appearance.skin = true; } });
  }, BASE);
  await setSettings({ appearance: { skin: true } });
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 15000 });
  check((await page.evaluate(() => window.__bcvMark === undefined)) && (await visible('#bcv-app')), 'the look button reloads and brings the interface back even when the copy that heard the change has no listener');
  await nav('courses');
  await page.waitForSelector('.bcv-ccard__hero--term', { timeout: 10000 });
  await setSettings({ appearance: { skin: false } });
  await page.waitForFunction(() => !document.documentElement.classList.contains('bcv-on'), null, { timeout: 5000 });
  // the class goes the moment the setting lands, but Canvas's own page is only there once the
  // reload it triggers has finished — wait for the page, not for the class
  check(await eventually(async () => { try { return page.url() === `${BASE}/courses` && (await visible('#application')) && (await page.title()).includes('courses'); } catch { return false; } }), 'look off after navigating shows the same page in stock Canvas (it was underneath all along)');
  // Turning the look off with an attempt on screen is not a reload: reloading would put the
  // browser's own "leave this page?" in the way and then land back on our quiz flow with nothing to
  // run it. It goes to Canvas's own take page, where every answer already saved is picked up.
  await page.goto(`${BASE}/courses/101/quizzes/9001`); // with the look still off, then turn it on here
  await page.waitForSelector('#application', { timeout: 10000 });
  await setSettings({ appearance: { skin: true } });
  await page.waitForSelector('.bcv-detail__title', { timeout: 20000 });
  await sw.evaluate(async () => (await fetch('http://localhost:8787/__mock/reopen-quiz', { method: 'POST', body: JSON.stringify({ quizId: '9001' }) })).ok);
  await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', func: () => { self.BCV.app.state.quizOpen = true; } });
  }, BASE);
  await setSettings({ appearance: { skin: false } });
  await page.waitForURL(`${BASE}/courses/101/quizzes/9001/take`, { timeout: 15000 });
  await page.waitForLoadState('domcontentloaded');
  check(page.url() === `${BASE}/courses/101/quizzes/9001/take` && (await visible('#application')) && !(await page.$('#bcv-app')) && (await page.$('#submit_quiz_form, .question_holder')) !== null, `the look off mid-attempt lands on Canvas's own quiz page rather than reloading ours: ${page.url()}`);
  await page.goto(`${BASE}/courses`); // the look is still off: back where the next check expects it
  await page.waitForSelector('#application', { timeout: 10000 });
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
  // the word-mark plays first (about two seconds), then the setup rises under it
  check(page.url() === `${BASE}/` && (await page.$('.bcv-stat')) !== null && (await page.$(su('.intro'))) !== null && (await page.$$(su('.rail__item'))).length === 4 && (await sStep()) === '1 of 4' && (await page.$eval('html', (e) => getComputedStyle(e).overflow)) === 'hidden', 'the setup opens over the dashboard on its own ground: the address cleaned, a word-mark, a rail of four steps, the page held still');
  // the dot after "Simpl" sits where the word ends in this system's font (here a Windows-like one,
  // wider than the Mac's: drawn at the design's fixed place it would land on the l)
  const dotOf = (hostSel) => page.evaluate((sel) => { const r = document.querySelector(sel).shadowRoot; const t = r.querySelector('.intro text'); const d = r.querySelector('.intro__dot'); const b = t.getBBox(); const vb = r.querySelector('.intro svg').getAttribute('viewBox').split(' ').map(Number); return { gap: Math.round(Number(d.getAttribute('cx')) - (b.x + b.width)), end: Math.round(b.x + b.width), cx: Number(d.getAttribute('cx')), fits: vb[2] >= Number(d.getAttribute('cx')) + 9 }; }, hostSel);
  const setupDot = await dotOf('#bcv-setup');
  check(setupDot.gap >= 10 && setupDot.gap <= 18 && setupDot.fits, `the dot after Simpl sits just past the word as drawn here, inside the drawing: ${JSON.stringify(setupDot)}`);
  await page.waitForFunction(() => document.querySelector('#bcv-setup')?.shadowRoot.querySelector('.intro')?.hidden === true, null, { timeout: 8000 });
  await page.waitForTimeout(500);
  await shot(page, '32-setup-over-page');
  const railNow = () => page.$$eval(su('.rail__item'), (els) => els.map((e) => `${e.querySelector('.rail__name').textContent}: ${e.querySelector('.rail__answer').textContent}${e.classList.contains('is-done') ? ' ✓' : ''}${e.disabled ? ' (locked)' : ''}`));
  const tickedNow = (await page.$$(su('.row.is-on'))).length; // the ones named like a class, ticked already (checked below)
  check(tickedNow > 0 && (await railNow()).join(' | ') === `Your courses: ${tickedNow} courses | Grades: Not yet (locked) | Dashboard: Not yet (locked) | Sidebar: Not yet (locked)`, `the rail names every step and really locks the ones ahead: ${(await railNow()).join(' | ')}`);
  const scanned = await texts(su('.row__code'));
  const total = scanned.length;
  const railAnswer = (step) => page.$eval(su(`.rail__item[data-step="${step}"] .rail__answer`), (e) => e.textContent);
  // Courses named like a class (a subject and a number: MATH 021, PHYS 008HL) are ticked for the
  // student; a resource site or a placement exam is not, whatever Canvas has starred. Continue is
  // alive as soon as one is ticked: the list picked here is the one every screen then follows.
  const CLASS = /\b[A-Z]{2,5}\s?-?\s?\d{1,4}[A-Z]{0,3}\b/;
  const rowsNow = await page.$$eval(su('.row[data-course]'), (els) => els.map((e) => ({ id: e.dataset.course, code: e.querySelector('.row__code').textContent.trim(), name: e.querySelector('.row__name').textContent.trim(), on: e.classList.contains('is-on') })));
  const classy = rowsNow.filter((r) => CLASS.test(r.code) || CLASS.test(r.name));
  const others = rowsNow.filter((r) => !classy.includes(r));
  check((await texts(su('.fr__h1')))[0] === 'Which courses are you in?' && scanned.length >= 8 && classy.length >= 5 && others.length >= 2 && rowsNow.every((r) => r.on === classy.includes(r)) && (await texts(su('.listhead span')))[0] === `${classy.length} of ${total} selected` && (await texts(su('#selectAll')))[0] === 'Select all' && !(await page.$eval(su('#next'), (b) => b.disabled)) && (await texts(su('#hint')))[0] === '' && (await railAnswer('courses')) === `${classy.length} courses`, `step 1 read the enrolments and ticked the ones named like a class: ${classy.map((r) => r.code).join(', ')} on; ${others.map((r) => r.name).join(' / ')} off`);
  // Clear all leaves none ticked, whatever was: Continue is dead with a hint until one is picked
  const clearAll = async () => { if ((await texts(su('#selectAll')))[0] === 'Select all') await page.click(su('#selectAll')); await page.click(su('#selectAll')); };
  await clearAll();
  check((await page.$$(su('.row.is-on'))).length === 0 && (await texts(su('.listhead span')))[0] === `0 of ${total} selected` && (await page.$eval(su('#next'), (b) => b.disabled)) && (await texts(su('#hint')))[0] === 'Pick at least one course.' && (await railAnswer('courses')) === 'None yet', 'Clear all unticks them all: Continue is dead with a hint until one is picked');
  // the list scrolls behind a scrollbar that is always drawn, so the courses below the fold are not
  // missed (headless Chromium hides every scrollbar, so the rule is read off the styles, not measured)
  const listBar = await page.$eval(su('.rows'), (e) => ({ bar: getComputedStyle(e, '::-webkit-scrollbar').width, scrolls: e.scrollHeight > e.clientHeight, mode: getComputedStyle(e).overflowY }));
  check(listBar.bar === '8px' && listBar.scrolls && listBar.mode === 'scroll', `the course list has a scrollbar that is always drawn, with more below: ${JSON.stringify(listBar)}`);
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
  check((await texts(su('.listhead span')))[0] === `5 of ${total} selected` && (await page.$$(su('.row.is-on'))).length === 5 && !(await page.$eval(su('#next'), (b) => b.disabled)) && (await texts(su('#hint')))[0] === '' && (await railAnswer('courses')) === '5 courses', 'rows toggle with the count, the rail answers, and Continue comes alive once one is picked');
  await page.click(su(`.row[data-course="${picks[0][0]}"]`));
  check((await texts(su('.listhead span')))[0] === `4 of ${total} selected` && (await page.$$(su('.row.is-on'))).length === 4 && (await railAnswer('courses')) === '4 courses', 'and a second press unticks it');
  await page.click(su('#selectAll'));
  check((await page.$$(su('.row.is-on'))).length === total && (await texts(su('#selectAll')))[0] === 'Clear all', 'Select all ticks every course and turns into Clear all');
  await page.click(su('#selectAll'));
  check((await page.$$(su('.row.is-on'))).length === 0 && (await page.$eval(su('#next'), (b) => b.disabled)) && (await railAnswer('courses')) === 'None yet', 'Clear all unticks them all, and Continue dies again');
  for (const [id] of picks) await page.click(su(`.row[data-course="${id}"]`));
  // a nickname typed on a row is saved with the rest (Canvas's own nickname); the field shows on
  // a ticked row only, and typing in it does not toggle the row
  const nickRow = page.locator(su('.row.is-on')).last();
  const nickId = await nickRow.getAttribute('data-course');
  await nickRow.locator('.row__nick').fill('Setup nick');
  check((await page.$$(su('.row.is-on'))).length === 5 && (await page.$eval(su('.row.is-on .row__nick'), (e) => e.placeholder)) === 'Nickname' && !(await page.locator(su('.row:not(.is-on) .row__nick')).first().isVisible()), 'a ticked row has a Nickname field (an unticked one does not); typing in it does not toggle the row');
  const firstColour = await page.$eval(su('.row.is-on .row__dot'), (e) => e.style.background); // the first chosen course's colour: the preview tiles are drawn in it
  await shot(page, '32c-setup-courses');
  await sNext('#track');
  const firstTargets = await page.$$eval(su('.target .seg button.is-on'), (bs) => bs.map((b) => b.textContent));
  check((await sStep()) === '2 of 4' && (await page.$eval(su('#track'), (e) => e.classList.contains('is-on'))) && (await texts(su('#goal')))[0] === '4.00' && (await page.$$(su('.target'))).length === 5 && firstTargets.join(',') === 'A+,A+,A+,A+,A+' && (await page.$$eval(su('.target:first-child .seg button'), (bs) => bs.map((b) => b.textContent))).join(' ') === 'C B B+ A- A A+', `step 2: tracking on, a 4.00 goal, every course aiming at A+, letters low to high: ${firstTargets.join(',')}`);
  check((await railNow()).join(' | ') === `Your courses: 5 courses ✓ | Grades: Tracking · goal 4.00 | Dashboard: Not yet (locked) | Sidebar: Not yet (locked)` && (await texts(su('.target__code'))).includes('Setup nick'), `the rail ticks step 1 with its answer, and the target rows carry the nickname typed there: ${(await railNow()).join(' | ')}`);
  await page.click(su('.stepper button:last-child')); // already at the top of the scale: it stays there
  check((await texts(su('#goal')))[0] === '4.00', `the goal does not climb past 4.00: ${(await texts(su('#goal')))[0]}`);
  await page.click(su('.stepper button:first-child'));
  await page.click(su('.stepper button:first-child'));
  await page.click(su('.target:first-child .seg button:nth-child(3)'));
  check((await texts(su('#goal')))[0] === '3.90' && (await page.$eval(su('.target:first-child .seg button.is-on'), (b) => b.textContent)) === 'B+' && (await railAnswer('grades')) === 'Tracking · goal 3.90', 'the goal stepper and a target pick, the rail following the goal');
  await page.click(su('#track'));
  check(!(await page.$eval(su('#track'), (e) => e.classList.contains('is-on'))) && (await page.$eval(su('#goalPanel'), (e) => e.classList.contains('is-hidden'))) && (await railAnswer('grades')) === 'Not tracking', 'history off hides the goal, and the rail reads Not tracking');
  await page.click(su('#track'));
  check((await page.$eval(su('#track'), (e) => e.classList.contains('is-on'))) && !(await page.$eval(su('#goalPanel'), (e) => e.classList.contains('is-hidden'))) && (await texts(su('#goal')))[0] === '3.90', 'and on again brings the goal back as it was');
  await shot(page, '32d-setup-grades');
  await sNext('.tile[data-view]');
  // step 3: what the Dashboard shows first — three preview tiles drawn in the chosen courses' colours, one chosen
  const VIEW_OF = { cards: 'cards', planner: 'list', activity: 'activity' };
  const viewBefore = VIEW_OF[(await apiGet('/dashboard/view')).dashboard_view] || 'list';
  const dashTiles = () => page.$$eval(su('.tile[data-view]'), (els) => els.map((t) => `${t.dataset.view}${t.classList.contains('is-on') ? ' *' : ''} [${t.querySelector('.tile__label').textContent}]`));
  const tileWords = await page.$$eval(su('.tile[data-view]'), (els) => els.map((t) => `${t.querySelector('.tile__t').textContent} — ${t.querySelector('.tile__s').textContent}`));
  check((await sStep()) === '3 of 4' && tileWords.join(' | ') === 'Cards — Courses as tiles, with what is due next. | List — Everything due, day by day, with a tick. | Activity — Announcements, replies and grades as they arrive.' && (await dashTiles()).join(' | ') === ['cards', 'list', 'activity'].map((v) => `${v}${v === viewBefore ? ' * [Selected]' : ' [Choose]'}`).join(' | '), `step 3 asks what the Dashboard shows first, the tile Canvas has (${viewBefore}) selected: ${(await dashTiles()).join(' | ')}`);
  check((await page.$$(su('.tile[data-view="cards"] .mini__card'))).length === 4 && (await page.$eval(su('.tile[data-view="cards"] .mini__card'), (e) => e.style.background)) === firstColour && (await page.$$(su('.tile[data-view="list"] .mini__row'))).length === 4 && (await page.$$(su('.tile[data-view="activity"] .mini__row'))).length === 3, `the tiles are miniatures of the real layouts in the chosen courses' own colours (${firstColour})`);
  const other = viewBefore === 'cards' ? 'activity' : 'cards';
  await page.click(su(`.tile[data-view="${other}"]`));
  check((await dashTiles()).join(' | ') === ['cards', 'list', 'activity'].map((v) => `${v}${v === other ? ' * [Selected]' : ' [Choose]'}`).join(' | ') && (await railAnswer('dashboard')) === { cards: 'Cards', list: 'List', activity: 'Activity' }[other] && VIEW_OF[(await apiGet('/dashboard/view')).dashboard_view] === viewBefore, 'one tile at a time, the rail says which, and nothing is written until the end');
  await shot(page, '32e-setup-dashboard');
  await page.click(su(`.tile[data-view="${viewBefore}"]`)); // back to what Canvas had: the pages after this read it
  await sNext('.tile[data-value]');
  // step 4: where the courses chosen in step 1 should sit
  const sideBefore = (await sw.evaluate(async () => (await self.BCV.settings.get()).appearance.sideCourses)) === 'always' ? 'always' : 'hover';
  const sideTiles = () => page.$$eval(su('.tile[data-value]'), (els) => els.map((t) => `${t.querySelector('.tile__t').textContent}${t.classList.contains('is-on') ? ' *' : ''}`));
  check((await sStep()) === '4 of 4' && (await sideTiles()).join(' | ') === `Always listed${sideBefore === 'always' ? ' *' : ''} | On hover${sideBefore === 'hover' ? ' *' : ''}` && (await page.$eval(su('#next'), (e) => e.textContent.trim())) === 'Finish', `step 4 asks where the courses live, the setting's own choice selected (${sideBefore}): ${(await sideTiles()).join(' | ')}`);
  await shot(page, '32f-setup-sidebar');
  const pickSide = async (v) => {
    await page.click(su(`.tile[data-value="${v}"]`));
    await page.waitForFunction((val) => document.querySelector('#bcv-setup').shadowRoot.querySelector(`.tile[data-value="${val}"]`).classList.contains('is-on'), v, { timeout: 5000 });
    return sw.evaluate(async () => (await self.BCV.settings.get()).appearance.sideCourses);
  };
  check((await pickSide('hover')) === sideBefore && (await railAnswer('sidebar')) === 'On hover', 'picking one moves the tick and the rail answer, and writes nothing yet');
  check((await pickSide('always')) === sideBefore && (await page.$$(su('.tile[data-value].is-on'))).length === 1 && (await railAnswer('sidebar')) === 'Always listed', 'and changing the pick moves the tick, one at a time'); // the sidebar list: the checks below read it
  await sNext('.summary__row');
  // Ready: a read-back of every answer, then Open Canvas writes them all at once
  const summary = await page.$$eval(su('.summary__row'), (els) => els.map((r) => `${r.querySelector('.summary__k').textContent}: ${r.querySelector('.summary__v').textContent}`));
  check((await sStep()) === 'Ready' && (await texts(su('.fr__h1')))[0] === 'You’re set' && summary.join(' | ') === `Courses shown: 5 of ${total} | Grade history: On · goal 3.90 | Dashboard: ${{ cards: 'Cards', list: 'List', activity: 'Activity' }[viewBefore]} | Sidebar: Always listed` && (await page.$eval(su('#next'), (e) => e.textContent.trim())) === 'Open Canvas' && (await page.$(su('#back'))) !== null && (await page.$$(su('.rail__item.is-done'))).length === 4, `Finish shows the read-back with every rail step ticked: ${summary.join(' | ')}`);
  await shot(page, '32g-setup-ready');
  await page.click(su('#back'));
  await page.waitForSelector(su('.tile[data-value]'), { timeout: 10000 });
  check((await sStep()) === '4 of 4' && (await railNow()).filter((r) => r.includes('(locked)')).length === 0, 'Back from the read-back returns to the last step, nothing locked behind');
  await page.click(su('.rail__item[data-step="courses"]'));
  await page.waitForSelector(su('.row[data-course]'), { timeout: 10000 });
  check((await sStep()) === '1 of 4' && (await page.$$(su('.row.is-on'))).length === 5 && (await page.$eval(su(`.row[data-course="${nickId}"] .row__nick`), (e) => e.value)) === 'Setup nick' && (await railNow()).join(' | ') === `Your courses: 5 courses | Grades: Tracking · goal 3.90 ✓ | Dashboard: ${{ cards: 'Cards', list: 'List', activity: 'Activity' }[viewBefore]} ✓ | Sidebar: Always listed ✓`, `the rail goes back to any step done, with its answers kept: ${(await railNow()).join(' | ')}`);
  for (const s of ['#track', '.tile[data-view]', '.tile[data-value]', '.summary__row']) await sNext(s);
  check((await sStep()) === 'Ready' && (await sw.evaluate(async () => (await self.BCV.api.storage.local.get('setup:done'))['setup:done'])) === undefined, 'forward again to the read-back: still nothing marked done');
  await page.click(su('#next'));
  await page.waitForSelector('.bcv-tour__card', { timeout: 20000 });
  check(page.url() === `${BASE}/` && (await page.$('#bcv-setup')) === null && !(await page.$('html.bcv-setup-open')) && (await page.$eval('.bcv-tour__title', (e) => e.textContent.trim())) === 'Your day at a glance', 'Open Canvas closes the setup and the tour starts on the same page');
  check((await sw.evaluate(async () => (await self.BCV.settings.get()).appearance.sideCourses)) === 'always' && VIEW_OF[(await apiGet('/dashboard/view')).dashboard_view] === viewBefore, 'the sidebar and dashboard choices were written on the way out');
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
  check((await tourTitle()) === 'Your day at a glance' && /^1 of 12$/i.test((await texts('.bcv-tour__count'))[0]) && (await covers('.bcv-stat')), `stop 1 spotlights the counters (${await rects()})`);
  const next = async (title) => { await page.click('.bcv-tour__btn.is-primary'); await page.waitForFunction((t) => document.querySelector('.bcv-tour__title')?.textContent.trim() === t, title, { timeout: 15000 }); };
  await next('Everything in one place');
  check(await ringOver('.bcv-nav'), 'stop 2 spotlights the sidebar');
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

  // ---- what's new after an update ----------------------------------------------------------------
  console.log("what's new");
  const wn = (sel) => `#bcv-whatsnew ${sel}`; // in a shadow root, like the setup
  const cur = whatsNew[0];
  const wnPage = () => page.evaluate(() => { const r = document.querySelector('#bcv-whatsnew').shadowRoot; return { since: r.querySelector('.wn__since')?.textContent ?? null, versions: [...r.querySelectorAll('.wn__vh')].map((v) => v.querySelector('.wn__vnum').textContent), dates: [...r.querySelectorAll('.wn__vdate')].map((d) => d.textContent), notes: [...r.querySelectorAll('.wn__note')].map((n) => `${n.dataset.version} ${n.dataset.kind}: ${n.querySelector('.wn__title').textContent}`), clutter: r.querySelectorAll('.wn__kind, .wn__where, .wn__filter, .wn__rail, .wn__jump, .fr__hint').length, more: r.querySelector('#earlier')?.textContent ?? null, foot: [...r.querySelectorAll('.fr__foot button')].map((b) => b.textContent.trim()), scroll: (() => { const l = r.querySelector('#notes'); return { over: l.scrollHeight > l.clientHeight, bar: getComputedStyle(l, '::-webkit-scrollbar').width }; })() }; });
  // an update from 2.7.5 (the background notes the version left behind), this version not yet seen:
  // one page lists every version since, newest first — a few skipped updates make one page, not one each
  const sinceOld = whatsNew.filter((v) => cmpVer(v.version, '2.7.5') > 0);
  await sw.evaluate(async () => { await self.BCV.api.storage.local.set({ 'whatsnew:from': '2.7.5' }); await self.BCV.api.storage.local.remove('whatsnew:seen'); });
  await page.goto(`${BASE}/`);
  await page.waitForSelector(wn('.wn__note'), { timeout: 20000 });
  const wnDot = await dotOf('#bcv-whatsnew');
  check(wnDot.gap >= 10 && wnDot.gap <= 18 && wnDot.fits, `its word-mark's dot sits just past the word too: ${JSON.stringify(wnDot)}`);
  await page.waitForFunction(() => document.querySelector('#bcv-whatsnew')?.shadowRoot.querySelector('.intro')?.hidden === true, null, { timeout: 8000 }); // the word-mark first
  await page.waitForTimeout(500);
  const wn1 = await wnPage();
  const expectNotes = sinceOld.flatMap((v) => v.notes.map((n) => `${v.version} ${n.kind}: ${n.title}`));
  check(wn1.since === 'Everything since 2.7.5' && sinceOld.length > 1 && wn1.versions.join(' | ') === sinceOld.map((v) => v.version).join(' | ') && wn1.dates.length === sinceOld.length && wn1.dates.every((d) => /\d{4}/.test(d)) && wn1.notes.join(' | ') === expectNotes.join(' | ') && wn1.clutter === 0 && wn1.more === 'Earlier versions' && wn1.foot.join(',') === 'Back to Canvas' && wn1.scroll.over && wn1.scroll.bar === '8px', `the first page after an update lists every version since the one left behind, newest first, with one button and a scrollbar for the rest: ${JSON.stringify(wn1)}`);
  check(page.url() === `${BASE}/` && (await page.$('.bcv-stat')) !== null && (await page.$eval('html', (e) => getComputedStyle(e).overflow)) === 'hidden', 'it sits over the page, which is drawn underneath and held still');
  await shot(page, '33-whats-new');
  // shown once: opening it is what marks the version seen, not closing it
  check(await eventually(async () => { const f = await sw.evaluate(() => self.BCV.api.storage.local.get(['whatsnew:seen', 'whatsnew:from'])); return f['whatsnew:seen'] === manifest.version && f['whatsnew:from'] === undefined; }), 'opening it marks the version seen at once');
  // Earlier versions appends the releases before the jump to the same list
  await page.click(wn('#earlier'));
  await page.waitForTimeout(400);
  const wn2 = await wnPage();
  const expectAll = [...sinceOld, ...whatsNew.filter((v) => cmpVer(v.version, '2.7.5') <= 0).slice(0, 8)];
  check(wn2.versions.join(' | ') === expectAll.map((v) => v.version).join(' | ') && wn2.notes.length === expectAll.reduce((n, v) => n + v.notes.length, 0) && (wn2.more === null) === (expectAll.length === whatsNew.length), `Earlier versions appends the ones before the jump, newest first: ${wn2.versions.join(' | ')}`);
  await shot(page, '33b-whats-new-earlier');
  // Back to Canvas lets the page go; the next page does not show it again
  await page.click(wn('#dismiss'));
  await page.waitForFunction(() => !document.querySelector('#bcv-whatsnew'), null, { timeout: 5000 });
  check(!(await page.$('html.bcv-setup-open')), 'Back to Canvas lets the page go');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  await page.waitForTimeout(600);
  check(!(await page.$('#bcv-whatsnew')), 'and the next page does not show it again');
  // closed any other way — the page reloaded while it was up — it is seen all the same: once is once
  await sw.evaluate(async () => { await self.BCV.api.storage.local.set({ 'whatsnew:from': '2.12.0' }); await self.BCV.api.storage.local.remove('whatsnew:seen'); });
  await page.goto(`${BASE}/`);
  await page.waitForSelector(wn('.wn__note'), { timeout: 20000 });
  await eventually(async () => (await sw.evaluate(() => self.BCV.api.storage.local.get('whatsnew:seen')))['whatsnew:seen'] === manifest.version);
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-app .bcv-nav__item', { timeout: 10000 });
  await page.waitForTimeout(600);
  check(!(await page.$('#bcv-whatsnew')), 'a page reloaded while it was up does not show it again either');
  // reachable again from the account menu, for this version alone
  await page.waitForSelector('#bcv-account', { timeout: 15000 });
  const openAccount = async () => { await page.click('#bcv-account'); return page.waitForSelector('.bcv-menu--account', { timeout: 3000 }).then(() => true, () => false); };
  if (!(await openAccount())) { await page.waitForTimeout(800); await openAccount(); } // a sidebar redraw can close a menu just opened
  const menuItems = await texts('.bcv-menu--account .bcv-menu__item');
  check(menuItems.some((t) => /What.s new/.test(t)), `the account menu has What’s new: ${menuItems.map((t) => t.split('\n')[0]).join(' | ')}`);
  await page.locator('.bcv-menu--account .bcv-menu__item').filter({ hasText: /What.s new/ }).click();
  await page.waitForSelector(wn('.wn__note'), { timeout: 10000 });
  const wn3 = await wnPage();
  check(wn3.since === null && wn3.versions.join(' | ') === manifest.version && wn3.notes.length === cur.notes.length && wn3.more === 'Earlier versions', `the account menu opens it again, for this version alone: ${JSON.stringify(wn3.versions)}`);
  await page.keyboard.press('Escape');
  await page.waitForFunction(() => !document.querySelector('#bcv-whatsnew'), null, { timeout: 5000 });
  check(!(await page.$('#bcv-whatsnew')), 'Escape closes it too');

  // ---- the page after install --------------------------------------------------------------
  console.log('setup page');
  const setup = await context.newPage();
  const sTexts = (sel) => setup.$$eval(sel, (els) => els.map((e) => (e.innerText || e.textContent).replace(/\s+/g, ' ').trim()));
  await setup.goto(`chrome-extension://${extId}/setup/setup.html`);
  await setup.waitForSelector('.welcome .h1', { timeout: 10000 });
  await setup.waitForTimeout(700); // the steps come in one after another
  const how = await sTexts('.how__t');
  check((await sTexts('.welcome .h1'))[0] === 'Simpl Courses is installed' && (await setup.$$('.blob')).length === 4 && how.join(' | ') === 'Open your Canvas | Open Simpl Courses from the toolbar | Press Set up', `the page after install says the three things to do: ${how.join(' | ')}`);
  // each step is drawn as well as said: the address bar on Canvas, the puzzle piece and the menu behind it, the popup's one button
  const pics = await setup.evaluate(() => [...document.querySelectorAll('.how__step')].map((s) => ({
    bar: s.querySelector('.pic__bar')?.textContent.trim() || null,
    hot: !!s.querySelector('.pic__ico--hot'),
    menu: s.querySelector('.pic__menu')?.textContent.trim() || null,
    btn: s.querySelector('.pic__btn')?.textContent.trim() || null,
    said: s.querySelector('.how__s').textContent,
  })));
  check(pics.length === 3 && pics[0].bar === 'yourschool.instructure.com' && pics[1].hot && pics[1].menu === 'Simpl Courses' && pics[2].btn === 'Set up', `and each is drawn: the address, the highlighted puzzle piece with Simpl Courses behind it, the Set up button: ${JSON.stringify(pics.map((x) => [x.bar, x.hot, x.menu, x.btn]))}`);
  check(/puzzle piece at the right of the address bar/.test(pics[1].said) && /pin beside it/.test(pics[1].said) && /asks once to allow Simpl Courses on that site/.test(pics[2].said), 'the words say where the puzzle piece is, what the pin does, and that Chrome asks once about the site');
  check((await setup.$eval('#next', (b) => b.textContent.trim())) === 'Got it', 'and the one button on the page just closes it');
  await setup.screenshot({ path: join(out, '32-setup-welcome.png') });
  await setup.close().catch(() => {});

  // ---- the account panel ----------------------------------------------------------------------------------
  console.log('account panel');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-account', { timeout: 15000 });
  await page.click('#bcv-account');
  await page.waitForSelector('.bcv-menu--account', { timeout: 5000 });
  const acctItems = await texts('.bcv-menu--account .bcv-menu__item');
  check(acctItems.map((t) => t.split('\n')[0].trim()).join(' | ') === 'Dark appearance | Simpl Courses settings Look, courses and grades | Guided setup Courses, grades and a tour | Tour What changed, on the real pages | What’s new What changed in this version | Canvas profile | All Canvas settings Profile, notifications, integrations | Notification preferences | Log out' && (await page.$eval('.bcv-menu--account', (m) => m.getBoundingClientRect().bottom <= window.innerHeight)), `the profile row opens a panel above itself: ${acctItems.join(' | ')}`);
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
  // getting unstuck: a screen whose answer never comes is given one fresh load after 15s (a reload
  // clears most of what wedges), remembered for the page so a second stall is shown, never looped on
  console.log('getting unstuck');
  let holdQuizzes = true;
  const quizzesRe = /\/api\/v1\/courses\/101\/quizzes(\?|$)/;
  await page.route(quizzesRe, async (route) => { if (holdQuizzes) return; await route.continue().catch(() => {}); }); // held: never answered while the hold is on
  await page.gotoRaw(`${BASE}/courses/101/quizzes`); // raw: this page never settles on its own
  const stallStart = Date.now();
  await page.waitForNavigation({ timeout: 30000 }); // the reload
  const stalledFor = Date.now() - stallStart;
  const reloadMark = await page.evaluate(() => JSON.parse(sessionStorage.getItem('bcv:reloaded') || 'null'));
  check(stalledFor >= 14000 && stalledFor <= 26000 && reloadMark && reloadMark.path === '/courses/101/quizzes', `a screen still waiting after 15s is loaded once more (after ${Math.round(stalledFor / 1000)}s), and the reload is remembered for the page: ${JSON.stringify(reloadMark)}`);
  // the hold is still on, so the fresh page stalls too: this time it is shown (Canvas's own page,
  // with the note) and not reloaded again — the reload a minute ago is remembered
  await page.waitForSelector('.bcv-toast', { timeout: 25000 });
  check(/Showing Canvas's own page: it took too long/.test((await texts('.bcv-toast')).join(' ')) && (await page.$('html.bcv-punch')) !== null && (await page.evaluate(() => JSON.parse(sessionStorage.getItem('bcv:reloaded')).at)) === reloadMark.at, `a second stall within the minute is shown, not reloaded again: ${(await texts('.bcv-toast')).join(' | ')}`);
  holdQuizzes = false;
  await page.unroute(quizzesRe);
  await page.goto(`${BASE}/courses/101/quizzes`);
  await page.waitForSelector('.bcv-body .bcv-row', { timeout: 20000 });
  check(!(await page.$('html.bcv-punch')), 'and once Canvas answers again the screen draws as usual');
  // a Canvas session that has ended: the first "unauthenticated" answer sends the page to sign in
  // again (a reload, once — it lands on Canvas's sign-in on a real site), every other request is
  // failed at once rather than each screen stalling on its own, and a second time within the
  // minute it is said rather than looped on
  await mockConfig({ sessionLost: true });
  const apiCalls = [];
  const countApi = (r) => { if (/\/api\/v1\//.test(r.url())) apiCalls.push(r.url()); };
  page.on('request', countApi);
  await page.gotoRaw(`${BASE}/`);
  await page.waitForNavigation({ timeout: 20000 }); // the reload
  await page.waitForSelector('.bcv-toast', { timeout: 20000 });
  const sessionMark = await page.evaluate(() => JSON.parse(sessionStorage.getItem('bcv:reloaded') || 'null'));
  const atNote = apiCalls.length; // the shell's first wave was already out when the first answer came; nothing may follow it
  await page.waitForTimeout(1500);
  page.off('request', countApi);
  check(/Your Canvas session has ended\. Reload the page to continue\./.test((await texts('.bcv-toast')).join(' ')) && sessionMark?.path === '/' && apiCalls.length === atNote, `a session that has ended is noticed at the first answer: one reload, then the note, and nothing more is asked of Canvas (${apiCalls.length - atNote} requests after the note): ${(await texts('.bcv-toast')).join(' | ')}`);
  await mockConfig({ sessionLost: false });
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 20000 });
  check(!(await page.$('.bcv-toast')), 'signed in again, the page is itself again');
  // a page left sitting three minutes or more is stale: coming back to the tab reloads it, so the
  // session is renewed and every screen is drawn again; where the tab never went away, the first
  // press stands in for that. The clock the page keeps lives in the extension's own world, so the
  // test winds it back from there.
  const windBack = (ms) => sw.evaluate(async ([base, back]) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', args: [back], func: (b) => { self.BCV.app.state.lastHere = Date.now() - b; } });
  }, [BASE, ms]);
  await page.evaluate(() => sessionStorage.removeItem('bcv:reloaded')); // the checks above already reloaded this path
  await windBack(0); // wide awake
  const awakeNav = page.waitForNavigation({ timeout: 2500 }).then(() => true).catch(() => false);
  await page.click('.bcv-stat');
  check(!(await awakeNav) && !!(await page.$('.bcv-sheet-ov')), 'a press while the page is awake does what it says, and reloads nothing');
  await page.click('.bcv-sheet-ov', { position: { x: 5, y: 5 } }); // the scrim closes it, as everywhere else in the suite
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet-ov'), null, { timeout: 5000 });
  await windBack(4 * 60 * 1000); // away since before the three minutes
  const awayNav = page.waitForNavigation({ timeout: 15000 }).then(() => true).catch(() => false);
  const pressedAt = Date.now();
  await page.click('.bcv-stat');
  // the reload is announced first: a pill floats down at the top — a dial counting three seconds down in orange, "Away Refresh", "Click to cancel"
  await page.waitForSelector('#bcv-away.is-in', { timeout: 3000 });
  await page.waitForFunction(() => { const e = document.querySelector('#bcv-away'); return !!e && e.getBoundingClientRect().top >= 0; }, null, { timeout: 2000 }); // the float-down settles
  const pill = await page.$eval('#bcv-away', (e) => { const ring = getComputedStyle(e.querySelector('.bcv-away__ring')); const hand = getComputedStyle(e.querySelector('.bcv-away__hand')); const r = e.getBoundingClientRect(); return { title: e.querySelector('.bcv-away__title').textContent, hint: e.querySelector('.bcv-away__hint').textContent, hintDim: getComputedStyle(e.querySelector('.bcv-away__hint')).color !== getComputedStyle(e.querySelector('.bcv-away__title')).color, ring: ring.stroke, ringAnim: `${ring.animationName}@${ring.animationDuration}`, handFill: hand.fill, handAnim: `${hand.animationName}@${hand.animationDuration}`, ticks: e.querySelectorAll('.bcv-away__tick, .bcv-away__pin').length, hands: e.querySelectorAll('.bcv-away__hand').length, top: Math.round(r.top), centred: Math.abs((r.left + r.right) / 2 - window.innerWidth / 2) < 2, fixed: getComputedStyle(e).position === 'fixed' }; });
  check(pill.title === 'Away Refresh' && pill.hint === 'Click to cancel' && pill.hintDim && pill.ring === 'rgb(255, 159, 10)' && pill.ringAnim === 'bcv-away-ring@3s' && pill.handFill === 'rgb(255, 159, 10)' && pill.handAnim === 'bcv-away-sweep@3s' && pill.ticks === 0 && pill.hands === 1 && pill.fixed && pill.top >= 0 && pill.top < 40 && pill.centred && !(await page.$('.bcv-sheet-ov')), `after three minutes away the first press is swallowed and a pill floats down at the top, its dial counting three seconds down in orange: ${JSON.stringify(pill)}`);
  await shot(page, '35-away-refresh');
  check((await awayNav) && Date.now() - pressedAt >= 2800, `and the page reloads when the count runs out, not before (${Date.now() - pressedAt} ms after the press)`);
  await page.waitForSelector('.bcv-stat', { timeout: 20000 });
  check((await page.evaluate(() => JSON.parse(sessionStorage.getItem('bcv:reloaded') || 'null')))?.path === '/' && !(await page.$('.bcv-sheet-ov')), 'the reload is remembered (so it cannot loop) and the press it swallowed opened nothing');
  // and coming back to the tab is enough on its own: no press has to be spent on it — the same pill, then the reload
  const comeBack = () => sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', func: () => document.dispatchEvent(new Event('visibilitychange')) });
  }, BASE);
  await page.evaluate(() => sessionStorage.removeItem('bcv:reloaded'));
  await windBack(4 * 60 * 1000);
  const backNav = page.waitForNavigation({ timeout: 15000 }).then(() => true).catch(() => false);
  await comeBack();
  await page.waitForSelector('#bcv-away.is-in', { timeout: 3000 });
  check(await backNav, 'coming back to the tab after three minutes floats the pill down and reloads on its own');
  await page.waitForSelector('.bcv-stat', { timeout: 20000 });
  // a press on the pill stands the reload down, and the page counts as awake again: the next press acts as itself
  await page.evaluate(() => sessionStorage.removeItem('bcv:reloaded'));
  await windBack(4 * 60 * 1000);
  const cancelNav = page.waitForNavigation({ timeout: 4500 }).then(() => true).catch(() => false);
  await comeBack();
  await page.waitForSelector('#bcv-away.is-in', { timeout: 3000 });
  await page.click('#bcv-away .bcv-away__btn');
  check((await eventually(async () => !(await page.$('#bcv-away')))) && !(await cancelNav), 'Click to cancel: the pill goes and nothing reloads');
  const afterCancelNav = page.waitForNavigation({ timeout: 2500 }).then(() => true).catch(() => false);
  await page.click('.bcv-stat');
  check(!(await afterCancelNav) && !!(await page.$('.bcv-sheet-ov')) && !(await page.$('#bcv-away')), 'and the press after a cancel does what it says');
  await page.click('.bcv-sheet-ov', { position: { x: 5, y: 5 } });
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet-ov'), null, { timeout: 5000 });
  // a quiz is left completely alone by it — not a reload, and not a note either
  await page.goto(`${BASE}/courses/101/quizzes/9001?bcv=take`);
  await page.waitForSelector('.bcv-qz__begin', { timeout: 15000 });
  await page.evaluate(() => sessionStorage.removeItem('bcv:reloaded'));
  await windBack(4 * 60 * 1000);
  const quizNav = page.waitForNavigation({ timeout: 4000 }).then(() => true).catch(() => false);
  await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', func: () => document.dispatchEvent(new Event('visibilitychange')) });
  }, BASE);
  await page.click('.bcv-qz__h1');
  check(!(await quizNav) && !(await page.$('.bcv-toast')) && !(await page.$('#bcv-away')) && (await page.$('.bcv-qz__begin')) !== null, 'a quiz is left alone by the stale-page reload: no pill, nothing reloads and nothing is said');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 20000 });
  // and it never loops: away again within the minute says so instead of reloading again
  await page.evaluate(() => sessionStorage.removeItem('bcv:reloaded'));
  await windBack(4 * 60 * 1000);
  const loopNav = page.waitForNavigation({ timeout: 15000 }).then(() => true).catch(() => false);
  await page.click('.bcv-stat');
  await loopNav; // the one that is allowed, and leaves its mark
  await page.waitForSelector('.bcv-stat', { timeout: 20000 });
  await windBack(4 * 60 * 1000);
  await page.click('.bcv-stat');
  check((await eventually(async () => /You were away for a while\. Reload the page to continue\./.test((await texts('.bcv-toast')).join(' ')))) && !(await page.$('#bcv-away')), 'a second stale press within the minute asks rather than reloading again, and no pill counts down to nothing');
  await page.evaluate(() => sessionStorage.removeItem('bcv:reloaded'));
  await windBack(0);
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 20000 });
  // Coming back is not itself a sign of life: where the tab never went away at all — another window
  // simply sitting on top of it — no visibilitychange is ever sent, and the first press is what has
  // to notice. Reading does keep the page awake, so a scroll stands the reload down.
  await windBack(4 * 60 * 1000);
  await page.evaluate(() => window.dispatchEvent(new Event('scroll')));
  await new Promise((r) => setTimeout(r, 150));
  const scrolledNav = page.waitForNavigation({ timeout: 2500 }).then(() => true).catch(() => false);
  await page.click('.bcv-stat');
  check(!(await scrolledNav) && !!(await page.$('.bcv-sheet-ov')), 'reading keeps the page awake: a scroll stands the stale reload down');
  await page.click('.bcv-sheet-ov', { position: { x: 5, y: 5 } });
  await page.waitForFunction(() => !document.querySelector('.bcv-sheet-ov'), null, { timeout: 5000 });
  await page.evaluate(() => sessionStorage.removeItem('bcv:reloaded'));
  await windBack(0);
  // A load left lit for good — a tab put away mid-load, a reply that never came — used to make the
  // row it belonged to dead for the rest of the page's life: every press on it was read as a double
  // press. Past a few seconds it is plainly stuck, and the next press is taken as a fresh one.
  await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', func: () => {
      self.BCV.app.state.loadKey = 'courses';
      self.BCV.app.state.loadAt = Date.now() - 30000; // lit half a minute ago and never put out
    } });
  }, BASE);
  await page.click('.bcv-nav__item[data-nav="courses"]');
  await page.waitForSelector('[data-term]', { timeout: 15000 });
  check(page.url() === `${BASE}/courses`, 'a press on a row whose load never came back still works');
  // and on coming back to the tab, a wash left sweeping is put out rather than left lit
  await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', func: () => {
      self.BCV.app.state.loadKey = 'todo';
      self.BCV.app.state.loadAt = Date.now() - 30000;
      self.BCV.app.state.lastHere = Date.now(); // not the away case: this is only the stuck wash
      document.dispatchEvent(new Event('visibilitychange'));
    } });
  }, BASE);
  check(await eventually(async () => (await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    const [{ result }] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', func: () => self.BCV.app.state.loadKey });
    return result;
  }, BASE)) === null), 'coming back puts out a wash that was left sweeping while the tab was away');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 20000 });
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
  // the count is Canvas's own host plus every site allowed since (the mock, registered when the page after install found it)
  const siteN = 1 + ((await sw.evaluate(() => self.BCV.settings.get())).domains || []).length;
  check(/^Version \d+\.\d+/.test(await options.$eval('#version', (el) => el.textContent)) && (await oTexts('#statusText'))[0] === `${siteN} site${siteN === 1 ? '' : 's'}`, `settings show the version and the site count: ${await options.$eval('#version', (el) => el.textContent)} · ${(await oTexts('#statusText'))[0]}`);
  await options.screenshot({ path: join(out, '29-options-general.png') });
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
  // Appearance: the Dashboard's views, the same three the setup asks about
  check((await options.$$eval('#dashCards, #dashList, #dashActivity', (bs) => bs.map((b) => b.classList.contains('is-on')))).join(',') === 'true,true,true', 'the three dashboard switches start on');
  await options.click('#dashActivity');
  await options.waitForTimeout(250);
  check((await sw.evaluate(() => self.BCV.settings.get())).appearance.dashboard.activity === false && !(await options.$eval('#dashActivity', (b) => b.classList.contains('is-on'))), 'switching one off saves');
  await options.click('#dashCards');
  await options.waitForTimeout(250);
  await options.click('#dashList');
  await options.waitForTimeout(250);
  check((await sw.evaluate(() => self.BCV.settings.get())).appearance.dashboard.list !== false && (await options.$eval('#dashList', (b) => b.classList.contains('is-on'))), 'the last one on cannot be switched off');
  await options.click('#dashCards');
  await options.click('#dashActivity');
  await options.waitForTimeout(250);
  check((await sw.evaluate(() => self.BCV.settings.get())).appearance.dashboard.activity === true, 'and back on saves too');
  // Sites and data
  await options.click('.navlink[data-section="sites"]');
  check((await oTexts('.site__host'))[0] === '*.instructure.com' && (await options.$eval('#addDomain', (b) => b.disabled)), 'Canvas sites lists the built-in host; Add waits for an address');
  await options.fill('#newDomain', 'canvas.school');
  check(!(await options.$eval('#addDomain', (b) => b.disabled)), 'an address with a dot enables Add');
  await options.click('.navlink[data-section="data"]');
  await options.waitForSelector('.stat', { timeout: 5000 });
  check((await oTexts('.stat')).length === 3 && (await oTexts('.stat b'))[0] === 'none' && (await oTexts('.action .row__t')).join(' | ') === 'Export settings | Import settings | Reset everything', `Data & about: ${(await oTexts('.stat')).join(' | ')}`);
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
  // Opening the popup must not disturb the page behind it: the interface is not built a second time
  // and the screen on show is not drawn again (a second copy of everything, scrollable, was reported).
  await page.goto(`${BASE}/`);
  await page.waitForSelector('.bcv-stat', { timeout: 20000 });
  await page.evaluate(() => { document.querySelector('#bcv-app .bcv-screen').dataset.bcvMark = '1'; });
  const probePage = await context.newPage();
  await probePage.goto(`chrome-extension://${extId}/popup/popup.html`);
  await probePage.waitForTimeout(900);
  await probePage.close();
  await page.bringToFront();
  await page.waitForTimeout(400);
  const after = await page.evaluate(() => ({
    apps: document.querySelectorAll('#bcv-app').length,
    screens: document.querySelectorAll('#bcv-main > .bcv-screen').length,
    same: document.querySelector('#bcv-app .bcv-screen')?.dataset.bcvMark === '1',
  }));
  check(after.apps === 1 && after.screens === 1 && after.same, `opening the popup leaves the page alone — one interface, one screen, not drawn again: ${JSON.stringify(after)}`);
  // Safari puts a site's registered content scripts into the page again when the extension looks at
  // its permissions — which is what opening the popup does — so the scripts must survive being run
  // twice. Injected again here, deliberately: the second copy must build nothing.
  const twice = await sw.evaluate(async (base) => {
    const [tab] = await chrome.tabs.query({ url: `${base}/*` });
    const files = chrome.runtime.getManifest().content_scripts[1].js;
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'ISOLATED', files });
    const [{ result }] = await chrome.scripting.executeScript({
      target: { tabId: tab.id }, world: 'ISOLATED',
      func: () => ({ apps: document.querySelectorAll('#bcv-app').length, screens: document.querySelectorAll('#bcv-main > .bcv-screen').length, mark: document.querySelector('#bcv-app .bcv-screen')?.dataset.bcvMark === '1' }),
    });
    return result;
  }, BASE);
  check(twice.apps === 1 && twice.screens === 1 && twice.mark, `the scripts put into the page a second time build nothing: still one interface, the screen untouched: ${JSON.stringify(twice)}`);

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
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:done': true, 'whatsnew:seen': v }), manifest.version); // (seen: What's new is driven on purpose below, not over every page)
  popupPage = await context.newPage();
  await popupPage.goto(`chrome-extension://${extId}/popup/popup.html`);
  await popupPage.waitForTimeout(500);
  check((await popupPage.$eval('#setup-card', (el) => el.hidden)) && !(await hiddenIn('.section')), 'after setup the popup shows the switches again');
  // the look switch, and under it Persistent (off to begin with), which says what a press of the look switch does
  check((await popupPage.$eval('#skin', (el) => el.checked)) === true && (await popupPage.$eval('#persist', (el) => el.checked)) === false && /changes this page only/.test(await popupPage.$eval('#persist', (el) => el.closest('label').textContent)), 'the popup has the look switch with Persistent under it, off to begin with');
  await popupPage.click('label:has(#persist) .switch__track');
  check(await eventually(async () => (await sw.evaluate(async () => (await self.BCV.settings.get()).appearance.persistLook)) === true), 'Persistent on is saved');
  await popupPage.click('label:has(#persist) .switch__track');
  check(await eventually(async () => (await sw.evaluate(async () => (await self.BCV.settings.get()).appearance.persistLook)) === false), 'and off again');
  check(/^v\d+\.\d+/.test(await popupPage.$eval('#version', (el) => el.textContent)), `popup shows the version: ${await popupPage.$eval('#version', (el) => el.textContent)}`);
  check((await popupPage.$eval('#foot-setup', (el) => el.textContent)) === 'Guided setup', 'the popup links to the guided setup');
  // Safari refuses permissions.request() unless it is still the browser's idea of a user gesture,
  // and awaiting anything between the press and the call loses it ("Must be called during a user
  // gesture"), so nothing may be awaited in between. Chromium is lenient about this, so the rule
  // is read off the source rather than pressed for.
  {
    const src = readFileSync(join(root, 'extension', 'popup', 'popup.js'), 'utf8');
    const from = src.indexOf('Asking for permission');
    const to = src.indexOf('permissions.request(', from);
    const between = from >= 0 && to > from ? src.slice(from, to) : 'await';
    check(!between.includes('await'), 'the popup asks for the site permission straight from the press, awaiting nothing first (Safari refuses otherwise)');
  }
  await popupPage.screenshot({ path: join(out, '30-popup.png') });

  // ---- the setup cannot be skipped (last: finishing it here writes the site's preferences afresh) ----
  console.log('setup cannot be skipped');
  // The card cannot be skipped: there is no Skip, Escape does nothing, and leaving the page does
  // not get past it — until its steps are done, every page with the interface on opens it again.
  await sw.evaluate(() => self.BCV.api.storage.local.remove('setup:done'));
  await page.goto(`${BASE}/grades?bcv=setup`);
  await page.waitForSelector(su('.row'), { timeout: 20000 });
  check((await page.$(su('#skip'))) === null && (await page.$(su('.top__skip'))) === null, 'the card has no Skip');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(300);
  check((await page.$('#bcv-setup')) !== null && (await sStep()) === '1 of 4', 'Escape does not close it');
  await page.goto(`${BASE}/courses`); // walking away: the next page opens it again, over the Dashboard
  await page.waitForSelector(su('.row'), { timeout: 20000 });
  check(page.url() === `${BASE}/` && (await sStep()) === '1 of 4' && (await sw.evaluate(async () => (await self.BCV.api.storage.local.get('setup:done'))['setup:done'])) === undefined, `leaving the page does not get past it: the next page opens the card again, unfinished (${await sStep()})`);
  // the only way out is through: the steps, then the tour (the favourites already starred are the
  // ones picked, so finishing here changes nothing in Canvas for the sections that follow)
  const keepStarred = (await apiGet('/api/v1/courses?per_page=100')).filter((c) => c.is_favorite).map((c) => String(c.id));
  await clearAll(); // the ones named like a class come ticked: start from none, then exactly the starred
  for (const id of keepStarred) await page.click(su(`.row[data-course="${id}"]`));
  await page.click(su('#next'));
  await page.waitForSelector(su('#track'), { timeout: 10000 });
  await page.click(su('#next'));
  await page.waitForSelector(su('.tile[data-view]'), { timeout: 10000 });
  await page.click(su('#next'));
  await page.waitForSelector(su('.tile[data-value]'), { timeout: 10000 });
  await page.click(su('#next')); // Finish
  await page.waitForSelector(su('.summary__row'), { timeout: 10000 });
  await page.click(su('#next')); // Open Canvas
  await page.waitForFunction(() => !document.querySelector('#bcv-setup'), null, { timeout: 15000 });
  await page.waitForSelector('.bcv-tour__card', { timeout: 20000 });
  check((await sw.evaluate(async () => (await self.BCV.api.storage.local.get('setup:done'))['setup:done'])) === true, 'finishing the steps is what marks the setup done, and the tour follows');
  await page.keyboard.press('Escape'); // the tour can be left; the setup could not
  await page.waitForFunction(() => !document.querySelector('.bcv-tour__card'), null, { timeout: 5000 });
  await page.goto(`${BASE}/grades`);
  await page.waitForSelector('.bcv-gpa__hero', { timeout: 15000 });
  check((await page.$('#bcv-setup')) === null && (await texts('.bcv-gpa__hero-sub'))[0]?.includes('This term so far') && (await texts('.bcv-gpa__goal-s'))[0] === 'Goal 4.00 · set it in settings', 'once done the card stays away, and the Grades page tracks with the goal the setup set');

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
  check((await page.evaluate(() => localStorage.getItem('bcv:early'))) === null && !afterReset.some((k) => k.startsWith('prefs:')), `Reset everything clears the note on the open Canvas tab and the extension's own storage (left: ${afterReset.join(', ') || 'nothing'})`);
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

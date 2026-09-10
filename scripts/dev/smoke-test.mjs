#!/usr/bin/env node
// Loads the extension into headless Chromium against the mock Canvas server
// and exercises the main features, saving screenshots to scripts/dev/out/.
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
  const apiCalls = [];
  page.on('request', (r) => { if (r.url().includes('/api/v1/')) apiCalls.push(r.url()); });
  page.on('pageerror', (e) => console.log('  page error:', e.message));
  page.on('console', (m) => { if (m.type() === 'error' || m.type() === 'warning') console.log('  console:', m.type(), m.text()); });

  // --- dashboard --------------------------------------------------------------
  console.log('dashboard');
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-nav-todo', { timeout: 10000 });
  await page.waitForSelector('#bcv-nav-smart', { timeout: 10000 });
  check(await page.$('#bcv-nav-smart'), 'Smart nav item injected');
  // --- shell + custom dashboard ---------------------------------------------------
  check(await page.$eval('html', (h) => h.classList.contains('bcv-skin') && h.classList.contains('bcv-shell')), 'shell + skin classes applied');
  await page.waitForSelector('#bcv-side .bcv-side__course', { timeout: 10000 });
  check(await page.$eval('#header', (el) => getComputedStyle(el).display === 'none'), 'Canvas global nav hidden');
  const sideCourses = await page.$$eval('#bcv-side .bcv-side__course', (els) => els.map((e) => e.textContent.trim()));
  check(sideCourses.length === 3, `sidebar lists ${sideCourses.length} courses`);
  check(await page.$eval('#bcv-side .bcv-side__item.is-active', (el) => el.textContent.trim() === 'Home'), 'Home active in sidebar');
  check(await page.$$eval('#bcv-side .bcv-side__badge', (els) => els.some((e) => e.textContent.trim() === '2')), 'inbox unread badge in sidebar');
  check(await page.$('#bcv-side .bcv-side__avatar'), 'avatar rendered in sidebar');
  check(await page.$eval('#bcv-top .bcv-top__title', (el) => el.textContent.trim() === 'Home'), 'top bar title');
  check(await page.$('#bcv-top #bcv-nav-todo') && await page.$('#bcv-top #bcv-nav-smart'), 'To Do / Smart buttons live in the top bar');
  await page.waitForSelector('#bcv-dash .bcv-course', { timeout: 10000 });
  await page.waitForFunction(() => document.querySelectorAll('#bcv-dash .bcv-cg').length >= 3, null, { timeout: 10000 });
  check(await page.$eval('#dashboard', (el) => getComputedStyle(el).display === 'none'), 'Canvas dashboard hidden');
  const title = await page.$eval('#bcv-dash .bcv-dash__title', (el) => el.textContent);
  check(/^Good (morning|afternoon|evening|night), Sam/.test(title), `greeting: ${title}`);
  const metrics = await page.$$eval('#bcv-dash .bcv-metric', (els) => els.map((e) => e.textContent.replace(/\s+/g, ' ').trim()));
  check(metrics.length === 3 && /This week/.test(metrics[0]) && /Due today/.test(metrics[1]) && /87%/.test(metrics[2]), `metric rings: ${metrics.join(' | ')}`);
  const tiles = await page.$$eval('#bcv-dash .bcv-course', (els) => els.map((e) => ({ name: e.querySelector('.bcv-course__name').textContent, ring: e.querySelector('.bcv-course__ring .bcv-ring__label').textContent, rows: e.querySelectorAll('.bcv-next').length })));
  check(tiles.length === 3 && tiles[0].ring === '92%' && tiles[1].ring === '81%', `course tiles with grade rings: ${tiles.map((t) => `${t.name}=${t.ring}/${t.rows}`).join(', ')}`);
  check(tiles.every((t) => t.rows >= 1), 'next-up rows on every tile');
  const groups = await page.$$eval('#bcv-dash .bcv-cg', (els) => els.map((e) => ({ name: e.querySelector('.bcv-cg__name').textContent, tasks: [...e.querySelectorAll('.bcv-task__title')].map((t) => t.textContent) })));
  check(groups.length === 3, `to-do column grouped by ${groups.length} courses`);
  const allTasks = groups.flatMap((g) => g.tasks);
  check(allTasks.length === 5 && !allTasks.some((t) => /ungraded|Chapter 4|Guest lecture/.test(t)), `to-do column lists ${allTasks.length} graded due items only`);
  check(await page.$$eval('#bcv-dash .bcv-cg .bcv-ring', (els) => els.length === 3), 'progress ring on each course group');
  await page.click('#bcv-dash .bcv-todo-side__toggle input');
  await page.waitForTimeout(200);
  check(await page.$$eval('#bcv-dash .bcv-task', (els) => els.length === 8), 'toggle adds to-do-date/event items');
  await page.click('#bcv-dash .bcv-todo-side__toggle input');
  await page.waitForFunction(() => document.querySelectorAll('#bcv-dash .bcv-ann').length >= 3, null, { timeout: 10000 });
  check(await page.$$eval('#bcv-dash .bcv-ann', (els) => els.length === 3), 'announcements listed');
  check(await page.$eval('#right-side-wrapper', (el) => getComputedStyle(el).display === 'none'), 'Canvas dashboard sidebar hidden');
  check(await page.$eval('#footer', (el) => getComputedStyle(el).display === 'none'), 'footer hidden');
  const badge = await page.$eval('#bcv-nav-todo .bcv-nav-badge', (el) => ({ hidden: el.hidden, text: el.textContent }));
  check(!badge.hidden && Number(badge.text) >= 1, `to-do nav badge = ${badge.text}`);
  await page.waitForSelector('.bcv-toast', { timeout: 5000 }).catch(() => null);
  check(await page.$('.bcv-toast'), 'reminder toast shown');
  await page.screenshot({ path: join(out, '01-dashboard-light.png') });

  // to-do panel
  await page.keyboard.press('t');
  await page.waitForSelector('#bcv-todo.is-open', { timeout: 5000 });
  const todoItems = await page.$$eval('#bcv-todo .bcv-todo__item', (els) => els.length);
  check(todoItems >= 7, `to-do panel lists ${todoItems} items`);
  const types = await page.$$eval('#bcv-todo .bcv-todo__type', (els) => els.map((e) => e.textContent.trim()));
  check(types.includes('Assignment') && types.includes('Quiz') && types.includes('Graded discussion') && types.includes('Discussion') && types.includes('Page') && types.includes('Event'), `type labels: ${[...new Set(types)].join(', ')}`);
  const badgeTexts = await page.$$eval('#bcv-todo .bcv-due-badge', (els) => els.map((e) => e.textContent.trim()));
  check(badgeTexts.some((t) => t.startsWith('To-do')) && badgeTexts.some((t) => t.startsWith('Due')), 'to-do panel distinguishes "Due" from "To-do" dates');
  await page.click('#bcv-todo .bcv-seg__btn[data-filter="discussion"]');
  await page.waitForTimeout(200);
  const discTypes = await page.$$eval('#bcv-todo .bcv-todo__type', (els) => els.map((e) => e.textContent.trim()));
  check(discTypes.length === 2 && discTypes.every((t) => /discussion/i.test(t)), `discussion filter shows ${discTypes.length} discussions only (${discTypes.join(', ')})`);
  await page.screenshot({ path: join(out, '02b-todo-filter-discussions.png') });
  await page.click('#bcv-todo .bcv-seg__btn[data-filter="all"]');
  await page.waitForTimeout(400);
  check(await page.$eval('#dashboard', (el) => getComputedStyle(el).display === 'none'), 'custom dashboard survives a settings change');
  await page.click('#bcv-todo .bcv-seg__btn[data-group="course"]');
  await page.waitForTimeout(300);
  const courseGroups = await page.$$eval('#bcv-todo .bcv-todo__group--course', (els) => els.map((e) => e.querySelector('.bcv-todo__group-name').textContent));
  check(courseGroups.length === 3 && !!(await page.$('#bcv-todo .bcv-todo__group--course .bcv-ring')), `panel grouped by course with rings: ${courseGroups.join(', ')}`);
  await page.screenshot({ path: join(out, '02c-todo-by-course.png') });
  await page.click('#bcv-todo .bcv-seg__btn[data-group="date"]');
  await page.waitForTimeout(300);
  await page.fill('#bcv-todo input[type="text"]', 'Email professor about extension');
  await page.press('#bcv-todo input[type="text"]', 'Enter');
  await page.waitForTimeout(300);
  check(await page.$$eval('#bcv-todo .bcv-todo__item', (els) => els.some((e) => e.textContent.includes('Email professor'))), 'custom task added');
  let rows = await page.$$('#bcv-todo .bcv-todo__item');
  for (const row of rows) {
    if ((await row.textContent()).includes('Email professor')) {
      await (await row.$('.bcv-todo__check')).click();
      break;
    }
  }
  await page.waitForTimeout(400);
  check(await page.$$eval('#bcv-todo .bcv-todo__item', (els) => !els.some((e) => e.textContent.includes('Email professor'))), 'completed custom task hidden (hide completed on)');
  // check off a Canvas item too (syncs to the planner override API)
  rows = await page.$$('#bcv-todo .bcv-todo__item');
  for (const row of rows) {
    if ((await row.textContent()).includes('Quiz 2')) {
      await (await row.$('.bcv-todo__check')).click();
      break;
    }
  }
  await page.waitForTimeout(800);
  check(await page.$$eval('#bcv-todo .bcv-todo__item', (els) => !els.some((e) => e.textContent.includes('Quiz 2'))), 'Canvas item checked off via planner override');
  await page.screenshot({ path: join(out, '02-todo-panel.png') });
  await page.keyboard.press('Escape');

  // palette
  await page.keyboard.press('Control+k');
  await page.waitForSelector('.bcv-palette__input', { timeout: 3000 });
  await page.type('.bcv-palette__input', 'cs grades');
  await page.waitForTimeout(200);
  const first = await page.$eval('.bcv-palette__item.is-active .bcv-palette__label', (el) => el.textContent);
  check(/CS 303 · Grades/.test(first), `palette top match: ${first}`);
  await page.screenshot({ path: join(out, '03-palette.png') });
  await page.keyboard.press('Escape');

  // help
  await page.keyboard.press('?');
  await page.waitForSelector('.bcv-help', { timeout: 3000 });
  await page.screenshot({ path: join(out, '04-help.png') });
  await page.keyboard.press('Escape');

  // j/k and number keys
  await page.keyboard.press('j');
  check(await page.$('.bcv-course__name.bcv-focus'), 'j focuses first course tile');
  await page.keyboard.press('2');
  await page.waitForURL(`${BASE}/courses/202`, { timeout: 5000 });
  check(page.url().endsWith('/courses/202'), 'number key opens 2nd course');

  // g chord
  await page.waitForSelector('#bcv-nav-todo', { timeout: 10000 });
  await page.keyboard.press('g');
  await page.keyboard.press('a');
  await page.waitForURL(`${BASE}/courses/202/assignments`, { timeout: 5000 });
  check(page.url().endsWith('/courses/202/assignments'), 'g a goes to assignments');

  // --- dark mode + theme ------------------------------------------------------------
  console.log('dark mode');
  await setSettings({ appearance: { darkMode: 'on', theme: 'midnight', minimal: true } });
  await page.goto(`${BASE}/`);
  await page.waitForSelector('#bcv-dash .bcv-course', { timeout: 10000 });
  check(await page.$eval('html', (h) => h.classList.contains('bcv-dark') && h.classList.contains('bcv-side-collapsed')), 'dark mode + collapsed sidebar classes applied');
  check(await page.$eval('html', (h) => getComputedStyle(h).filter.includes('invert')), 'root invert filter active');
  check(await page.$eval('#bcv-side', (el) => el.getBoundingClientRect().width < 100), 'sidebar collapsed to a rail');
  await page.screenshot({ path: join(out, '05-dashboard-dark-minimal.png') });
  await page.keyboard.press('t');
  await page.waitForSelector('#bcv-todo.is-open');
  await page.screenshot({ path: join(out, '06-todo-dark.png') });
  await page.keyboard.press('Escape');
  await setSettings({ appearance: { darkMode: 'on', theme: 'default', minimal: false } });
  await page.goto(`${BASE}/courses/303/modules`);
  await page.waitForSelector('.bcv-due-badge', { timeout: 10000 });
  await page.screenshot({ path: join(out, '06b-modules-dark.png') });
  await setSettings({ appearance: { darkMode: 'off', theme: 'default', minimal: false } });

  // --- redesigned course pages ---------------------------------------------------------
  console.log('course pages');
  await page.goto(`${BASE}/courses/202`);
  await page.waitForSelector('#bcv-course-hero', { timeout: 10000 });
  await page.waitForSelector('#bcv-side .bcv-side__tab', { timeout: 10000 });
  check(await page.$eval('#left-side', (el) => getComputedStyle(el).display === 'none'), 'Canvas course menu hidden');
  const tabs = await page.$$eval('#bcv-side .bcv-side__tab', (els) => els.map((e) => ({ label: e.textContent.trim(), active: e.classList.contains('is-active') })));
  check(tabs.length === 11 && tabs.find((t) => t.active)?.label === 'Home' && tabs.some((t) => t.label === 'Zoom'), `course tabs from API in sidebar (${tabs.length}, active=${tabs.find((t) => t.active)?.label})`);
  const hero = await page.$eval('#bcv-course-hero', (el) => ({ title: el.querySelector('.bcv-ch__title').textContent, grade: el.querySelector('.bcv-ch__grade .bcv-ring__label').textContent, next: el.querySelectorAll('.bcv-next').length, mods: el.querySelectorAll('.bcv-mod').length, anns: el.querySelectorAll('.bcv-ann').length }));
  check(hero.title === 'HIST 202: Modern Europe' && hero.grade === '81%' && hero.next >= 1 && hero.mods === 2 && hero.anns === 1, `course overview: ${JSON.stringify(hero)}`);
  check(await page.$eval('#bcv-top .bcv-top__crumb', (el) => /HIST 202/.test(el.textContent)), 'top bar shows course crumb');
  await page.screenshot({ path: join(out, '13-course-home-skin.png') });
  await page.goto(`${BASE}/courses/303/modules`);
  await page.waitForSelector('.bcv-due-badge', { timeout: 10000 });
  const modBadges = await page.$$eval('.bcv-due-badge', (els) => els.map((e) => e.textContent.trim()));
  check(modBadges.some((b) => /^To-do/.test(b)), `modules: ungraded discussion shows a to-do badge (${modBadges.join(' | ')})`);
  check(modBadges.some((b) => /^Due|^Done/.test(b)), 'modules: graded items show due/done badges');
  await page.screenshot({ path: join(out, '14-modules-skin.png') });
  check(await page.$eval('#bcv-side .bcv-side__tab.is-active', (el) => el.textContent.trim() === 'Modules'), 'Modules tab active in sidebar');
  await page.goto(`${BASE}/courses/101/grades`);
  await page.waitForSelector('#bcv-nav-todo', { timeout: 10000 });
  check(await page.$eval('#grades_summary thead th', (el) => getComputedStyle(el).textTransform === 'uppercase'), 'grades table restyled');
  await page.screenshot({ path: join(out, '15-grades-skin.png') });

  // --- assignment list badges --------------------------------------------------------
  console.log('assignments');
  await page.goto(`${BASE}/courses/101/assignments`);
  await page.waitForSelector('.bcv-due-badge', { timeout: 10000 });
  const badges = await page.$$eval('.bcv-due-badge', (els) => els.map((e) => e.textContent.trim()));
  check(badges.some((b) => /Due in \d+h/.test(b)), `countdown badge: ${badges.join(' | ')}`);
  check(badges.some((b) => /Overdue/.test(b)), 'overdue badge present');
  await page.screenshot({ path: join(out, '07-assignments-badges.png') });

  // --- assignment page: embeds + smart sidebar -----------------------------------------
  console.log('assignment page');
  await page.goto(`${BASE}/courses/101/assignments/1`);
  await page.waitForSelector('.bcv-embed-btn', { timeout: 10000 });
  const embedBtns = await page.$$('.bcv-embed-btn');
  check(embedBtns.length === 2, `embed buttons: ${embedBtns.length} (src iframe + LTI form)`);
  const [popup] = await Promise.all([context.waitForEvent('page', { timeout: 5000 }), embedBtns[0].click()]);
  check(/external_tools\/retrieve/.test(popup.url()), `opened embed in new tab: ${popup.url()}`);
  await popup.close();

  await page.keyboard.press('s');
  await page.waitForSelector('#bcv-smart.is-open', { timeout: 5000 });
  await page.waitForFunction(() => document.querySelector('.bcv-smart__context-label')?.textContent.startsWith('Using:'), null, { timeout: 10000 });
  const ctxLabel = await page.$eval('.bcv-smart__context-label', (el) => el.textContent);
  check(/Problem Set 3/.test(ctxLabel), `context label: ${ctxLabel}`);
  const actions = await page.$$eval('.bcv-smart__action', (els) => els.map((e) => e.textContent));
  check(actions.includes('Explain my feedback'), `quick actions: ${actions.join(', ')}`);
  check(!(await page.$eval('.bcv-smart__setup', (el) => el.hidden)), 'setup card shown when no key configured');
  await page.screenshot({ path: join(out, '08-smart-sidebar-setup.png') });

  // with a (fake) key configured the composer is enabled and a request is attempted
  await setSettings({ smart: { claudeKey: 'sk-ant-test-not-real' } });
  await page.waitForFunction(() => document.querySelector('.bcv-smart__setup')?.hidden === true, null, { timeout: 5000 });
  await page.fill('#bcv-smart textarea', 'What is this assignment about?');
  await page.press('#bcv-smart textarea', 'Enter');
  await page.waitForSelector('.bcv-msg--user', { timeout: 5000 });
  await page.waitForSelector('.bcv-msg--error, .bcv-msg--assistant:not(.is-streaming)', { timeout: 30000 });
  const errText = await page.$eval('.bcv-msg--error, .bcv-msg--assistant', (el) => el.textContent);
  check(/Claude/.test(errText), `provider error surfaced in chat: ${errText.slice(0, 80)}`);
  await page.screenshot({ path: join(out, '09-smart-sidebar-chat.png') });
  await setSettings({ smart: { claudeKey: '' } });

  // the page context sent to the model (content-script world is not reachable from
  // page.evaluate, so verify via the API calls it makes and the label it renders)
  check(apiCalls.some((u) => /\/api\/v1\/courses\/101\/assignments\/1\/submissions\/self/.test(u)), 'assignment context fetched submission (rubric + comments)');

  // --- discussion page: insert draft ---------------------------------------------------
  console.log('discussion');
  await page.goto(`${BASE}/courses/202/discussion_topics/3`);
  await page.waitForSelector('#bcv-nav-smart', { timeout: 10000 });
  await page.keyboard.press('s');
  await page.waitForSelector('#bcv-smart.is-open');
  await page.waitForFunction(() => document.querySelector('.bcv-smart__context-label')?.textContent.startsWith('Using:'), null, { timeout: 10000 });
  check(/Industrialization/.test(await page.$eval('.bcv-smart__context-label', (el) => el.textContent)), 'discussion context loaded from the API');
  check(apiCalls.some((u) => /discussion_topics\/3\/view/.test(u)), 'discussion context fetched replies');
  const dactions = await page.$$eval('.bcv-smart__action', (els) => els.map((e) => e.textContent));
  check(dactions.includes('Draft a reply'), `discussion actions: ${dactions.join(', ')}`);

  // --- extension pages ----------------------------------------------------------------
  console.log('extension pages');
  const options = await context.newPage();
  await options.goto(`chrome-extension://${extId}/options/options.html`);
  await options.waitForSelector('#claudeKey', { timeout: 5000 });
  await options.waitForFunction(() => document.querySelectorAll('#themes .theme').length > 0, null, { timeout: 5000 });
  await options.fill('#openaiKey', 'sk-test');
  await options.dispatchEvent('#openaiKey', 'change');
  await options.waitForTimeout(300);
  const status = await options.$eval('#smartStatus', (el) => el.textContent);
  check(/ChatGPT/.test(status), `options status reflects key: ${status}`);
  await options.click('#testOpenAI');
  await options.waitForFunction(() => /rejected|Could not|works/.test(document.querySelector('#openaiResult').textContent), null, { timeout: 20000 });
  console.log('   key test result:', await options.$eval('#openaiResult', (el) => el.textContent));
  await options.fill('#openaiKey', '');
  await options.dispatchEvent('#openaiKey', 'change');
  await options.screenshot({ path: join(out, '10-options.png'), fullPage: true });
  await options.click('.navlink[data-section="appearance"]');
  await options.screenshot({ path: join(out, '11-options-appearance.png'), fullPage: true });

  const popupPage = await context.newPage();
  await popupPage.goto(`chrome-extension://${extId}/popup/popup.html`);
  await popupPage.waitForFunction(() => document.querySelectorAll('#theme option').length > 0, null, { timeout: 5000 });
  await popupPage.waitForTimeout(500);
  await popupPage.screenshot({ path: join(out, '12-popup.png') });
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

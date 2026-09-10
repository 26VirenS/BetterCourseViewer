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
  await page.waitForSelector('#bcv-due-strip .bcv-due-chip', { timeout: 10000 });
  const chips = await page.$$eval('#bcv-due-strip .bcv-due-strip__items:not(.bcv-due-strip__items--muted) .bcv-due-chip', (els) => els.map((e) => e.textContent.trim()));
  check(chips.length === 5, `due strip shows ${chips.length} graded due items (expected 5)`);
  check(!chips.some((c) => /ungraded|Chapter 4|Guest lecture/.test(c)), 'ungraded discussion / page / event are not listed as due');
  const scheduledChips = await page.$$eval('#bcv-due-strip .bcv-due-strip__items--muted .bcv-due-chip', (els) => els.map((e) => e.textContent.trim()));
  check(scheduledChips.length === 3, `scheduled row lists ${scheduledChips.length} to-do/event items (expected 3)`);
  // redesigned dashboard
  check(await page.$eval('html', (h) => h.classList.contains('bcv-skin')), 'redesigned interface class applied');
  await page.waitForSelector('#bcv-greeting', { timeout: 5000 });
  const greeting = await page.$eval('#bcv-greeting', (el) => el.textContent);
  check(/^Good (morning|afternoon|evening|night), Sam/.test(greeting) && /due this week/.test(greeting), `greeting: ${greeting.slice(0, 60)}`);
  check(await page.$eval('.ic-Dashboard-header__title', (el) => getComputedStyle(el).display === 'none'), 'original "Dashboard" title hidden');
  await page.waitForSelector('.ic-DashboardCard .bcv-card-next', { timeout: 5000 });
  const nextUp = await page.$$eval('.ic-DashboardCard', (cards) => cards.map((c) => ({ title: c.querySelector('.ic-DashboardCard__header-title')?.textContent.trim(), rows: [...c.querySelectorAll('.bcv-card-next__row')].map((r) => r.textContent.trim()) })));
  check(nextUp.every((c) => c.rows.length >= 1), `next-up rows on every card: ${nextUp.map((c) => `${c.title}=${c.rows.length}`).join(', ')}`);
  check(await page.$eval('.ic-DashboardCard', (el) => parseFloat(getComputedStyle(el).borderTopLeftRadius) >= 16), 'cards have rounded corners');
  check(await page.$eval('#header', (el) => getComputedStyle(el).backgroundColor === 'rgb(251, 251, 253)'), 'light sidebar applied');
  check(await page.$eval('html', (h) => h.classList.contains('bcv-hide-sidebar')), 'declutter classes applied');
  check(await page.$eval('#right-side-wrapper', (el) => getComputedStyle(el).display === 'none'), 'dashboard sidebar hidden');
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
  check(await page.$eval('.ic-Dashboard-header__title', (el) => getComputedStyle(el).display === 'none'), 'greeting survives a settings change');
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
  check(await page.$('.ic-DashboardCard__link.bcv-focus'), 'j focuses first dashboard card');
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
  await page.waitForSelector('#bcv-due-strip', { timeout: 10000 });
  check(await page.$eval('html', (h) => h.classList.contains('bcv-dark') && h.classList.contains('bcv-minimal')), 'dark + minimal classes applied');
  check(await page.$eval('html', (h) => getComputedStyle(h).filter.includes('invert')), 'root invert filter active');
  check(await page.$eval('html', (h) => getComputedStyle(h).getPropertyValue('--ic-brand-global-nav-bgd').trim() === '#0f172a'), 'theme nav colour applied');
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
  await page.waitForSelector('#bcv-nav-todo', { timeout: 10000 });
  check(await page.$eval('#section-tabs a.active', (el) => parseFloat(getComputedStyle(el).borderRadius) >= 8), 'course nav pills styled');
  check(await page.$eval('#content', (el) => parseFloat(getComputedStyle(el).borderTopLeftRadius) >= 16 && getComputedStyle(el).backgroundColor === 'rgb(255, 255, 255)'), 'content rendered as a rounded card');
  await page.screenshot({ path: join(out, '13-course-home-skin.png') });
  await page.goto(`${BASE}/courses/303/modules`);
  await page.waitForSelector('.bcv-due-badge', { timeout: 10000 });
  const modBadges = await page.$$eval('.bcv-due-badge', (els) => els.map((e) => e.textContent.trim()));
  check(modBadges.some((b) => /^To-do/.test(b)), `modules: ungraded discussion shows a to-do badge (${modBadges.join(' | ')})`);
  check(modBadges.some((b) => /^Due|^Done/.test(b)), 'modules: graded items show due/done badges');
  await page.screenshot({ path: join(out, '14-modules-skin.png') });
  await page.goto(`${BASE}/courses/101/grades`);
  await page.waitForSelector('#bcv-nav-todo', { timeout: 10000 });
  check(await page.$eval('#grades_summary', (el) => parseFloat(getComputedStyle(el).borderTopLeftRadius) >= 10), 'grades table rounded');
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

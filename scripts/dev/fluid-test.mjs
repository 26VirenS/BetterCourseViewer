#!/usr/bin/env node
// How soon a screen is there, and how it gets there: the time from a press (or a page load) to the
// screen's content on show, against a mock Canvas that answers slowly (every API answer held, the
// slowest kinds longer — POST /__mock/config apiDelay), for the Dashboard, a course's home, Grades and
// To Do: the first time in a page, again in the same page, and again on a fresh page load. Then the
// things that make it quick, each checked: a page loaded again draws from what was kept before Canvas
// has answered, and puts it right in place when Canvas does; the next screens' data is read at idle
// (never more than two of those requests out at once) and under a pointer resting on a link; and a
// move between screens is one view transition — deeper slides in from the right, Back from the left,
// a section crossfades — or none under reduced motion.
//
// Writes scripts/dev/out/fluid-<label>.json (label: --label <name>, default "now") so two runs can be
// compared. --measure-only prints the numbers and checks nothing (for a build from before any of this).
// Run: node scripts/dev/fluid-test.mjs [--label before] [--measure-only] [--reps 3] [--ext <another extension/ to measure>]
// (test-all.mjs runs it once through, as the suite `fluid`; the numbers in a report are from --reps 3)
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { afterMigration, launchExtension, shortenTimers, motionAt, MOTION_RATE } from './harness.mjs'; // (motionAt: the transitions are watched at real speed)
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const argOf = (name) => { const i = process.argv.indexOf(name); return i >= 0 ? process.argv[i + 1] : null; };
const LABEL = argOf('--label') || 'now';
const MEASURE_ONLY = process.argv.includes('--measure-only');
const REPS = Math.max(1, Number(argOf('--reps')) || 1); // (--reps 3 for numbers worth comparing: the median of three)
const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const out = join(root, 'scripts', 'dev', 'out');
mkdirSync(out, { recursive: true });
const PORT = 8815, SIM_PORT = 8816;
const BASE = `http://localhost:${PORT}`;
// a Canvas of ordinary speed: each answer a quarter of a second, a course's assignments (the slowest kind) longer
const LATENCY = { '/assignments$': 600, '': 250 };

const extDir = join(tmpdir(), `bcv-fluid-ext-${process.pid}`);
cpSync(argOf('--ext') || join(root, 'extension'), extDir, { recursive: true }); // (--ext <dir>: another build's extension/, measured the same way — a checkout from before, say)
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*')) cs.matches.push(`${BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
shortenTimers(extDir);
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT), String(SIM_PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };
const median = (xs) => { const s = xs.filter((x) => Number.isFinite(x)).sort((a, b) => a - b); return s.length ? s[Math.floor((s.length - 1) / 2)] : null; };
const ms = (x) => (x === null || x === undefined ? '—' : `${Math.round(x)} ms`);

// Each screen's content, as it is once Canvas's answer is drawn (never the skeleton that holds its place)
const SCREENS = {
  dashboard: { path: '/', nav: 'dashboard', sel: '.bcv-stats .bcv-stat' },
  todo: { path: '/#todo', nav: 'calendar', sel: '.bcv-cal__todo .bcv-tdrag' }, // (2.99.112: To Do beside the calendar in To Do & Calendar — its row, or To Do's old address)
  grades: { path: '/grades', nav: 'gpa', sel: '.bcv-gpa__card' },
  course: { path: '/courses/101', fav: '101', sel: '.bcv-cmain .bcv-col:first-child > :not(.bcv-skel)' }, // (its home's own column: the front page)
};
// (in the page's own world, from its first line: the first frame each screen's content is on show,
// counted from the navigation's start — and, armed before a press, from the press)
const PROBE = `(() => {
  const want = ${JSON.stringify(Object.fromEntries(Object.entries(SCREENS).map(([k, v]) => [k, v.sel])))};
  const p = window.__bcvProbe = { first: {}, sel: null, t0: 0, hit: null, armed: false, arm(sel) { this.sel = null; this.want = sel; this.armed = true; this.hit = null; } };
  // (the clock starts at the press itself, not when the harness asks for it: the harness's own waits are not the interface's)
  addEventListener('pointerdown', (e) => { if (p.armed) { p.armed = false; p.t0 = e.timeStamp; p.sel = p.want; } }, true);
  const shown = (sel) => { const main = document.getElementById('bcv-main'); const el = main && [...main.querySelectorAll(sel)].find((e) => !e.closest('[data-old], .bcv-screen--skel, .bcv-skel')); return !!el; };
  const loop = () => {
    for (const [k, sel] of Object.entries(want)) if (p.first[k] === undefined && shown(sel)) p.first[k] = performance.now();
    if (p.sel && p.hit === null && shown(p.sel)) p.hit = performance.now() - p.t0;
    requestAnimationFrame(loop);
  };
  requestAnimationFrame(loop);
  // (and whether the page arrived on a cross-document view transition, and whether it ran)
  addEventListener('pagereveal', (e) => { const r = window.__bcvReveal = { vt: !!e.viewTransition, ran: null }; e.viewTransition?.ready.then(() => { r.ran = true; }, () => { r.ran = false; }); });
})();`;

const userDataDir = join(tmpdir(), `bcv-fluid-profile-${process.pid}`);
const { context, sw } = await launchExtension(chromium, userDataDir, extDir, { viewport: { width: 1400, height: 900 } });
await context.addInitScript(PROBE);
const result = { label: LABEL, at: new Date().toISOString(), version: manifest.version, latency: LATENCY, reps: REPS, numbers: {} };
const t0 = Date.now();
try {
  await afterMigration(sw);
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'welcome:report1': true, 'tools:welcomed': true, 'welcome:grades': true, 'setup:flow': 3, 'whatsnew:seen': v }), manifest.version);
  for (const p of context.pages()) if (p.url().endsWith('/setup/setup.html')) await p.close();
  const mockConfig = (cfg) => fetch(`${BASE}/__mock/config`, { method: 'POST', body: JSON.stringify(cfg) });
  /** What was kept between page loads (the answers drawn from at once), forgotten: the next load is a first one. */
  // (the page on show is left first: answers that land as a page is left are kept as it goes)
  const forgetKept = async () => { await page.goto('about:blank').catch(() => {}); await forgetKeptNow(); };
  const forgetKeptNow = () => sw.evaluate(async () => { const all = await self.BCV.api.storage.local.get(null); const keys = Object.keys(all).filter((k) => k.startsWith('kept:')); if (keys.length) await self.BCV.api.storage.local.remove(keys); });
  const page = await context.newPage();
  page.on('pageerror', (e) => console.log('  page error:', e.message));
  const settled = (page) => page.waitForFunction(() => document.documentElement.classList.contains('bcv-settled'), null, { timeout: 20000 }).catch(() => {});
  const quiet = (page, n = 1500) => page.waitForTimeout(n);
  await mockConfig({ apiDelay: LATENCY });

  /** A fresh page load of a screen: from the navigation's start to its content. */
  async function load(key) {
    await page.goto('about:blank'); // (a page of its own: '/#todo' after '/' would be the same document)
    await page.goto(`${BASE}${SCREENS[key].path}`, { waitUntil: 'commit' });
    await page.waitForFunction((k) => window.__bcvProbe?.first[k] !== undefined, key, { timeout: 20000 }).catch(() => {});
    const t = await page.evaluate((k) => window.__bcvProbe?.first[k] ?? null, key);
    await settled(page);
    return t;
  }
  /** A press in the sidebar (a root screen's row, or a favourite course's), from the press to the content. */
  async function press(key) {
    const s = SCREENS[key];
    const sel = s.nav ? `.bcv-nav__item[data-nav="${s.nav}"]` : `.bcv-fav[data-load="fav:${s.fav}"]`;
    await page.waitForSelector(sel, { timeout: 10000 });
    await page.evaluate((x) => { for (const m of document.querySelectorAll('#bcv-main > *')) m.dataset.old = '1'; window.__bcvProbe.arm(x); }, s.sel);
    await page.click(sel);
    await page.waitForFunction(() => window.__bcvProbe.hit !== null, null, { timeout: 20000 }).catch(() => {});
    const t = await page.evaluate(() => window.__bcvProbe.hit);
    await settled(page);
    return t;
  }

  console.log(`fluid: ${LABEL} (v${manifest.version}), every API answer held ${LATENCY['']} ms (a course's assignments ${LATENCY['/assignments$']} ms), ${REPS} runs each`);
  const runs = { firstLoad: {}, firstPress: {}, againPress: {}, againLoad: {} };
  for (const k of Object.keys(SCREENS)) for (const r of Object.values(runs)) r[k] = [];
  for (let rep = 0; rep < REPS; rep++) {
    // a page load with nothing kept from before: the first visit
    for (const k of Object.keys(SCREENS)) { await forgetKept(); runs.firstLoad[k].push(await load(k)); await quiet(page, 400); }
    // one page, every screen pressed in turn after a moment on the one before (the page idle meanwhile), then again
    await forgetKept();
    await load('dashboard');
    await quiet(page, 2500);
    for (const k of ['todo', 'grades', 'course']) { runs.firstPress[k].push(await press(k)); await quiet(page, 2500); }
    runs.againPress.dashboard.push(await press('dashboard'));
    await quiet(page, 2500);
    runs.firstPress.dashboard.push(null);
    for (const k of ['todo', 'grades', 'course']) { runs.againPress[k].push(await press(k)); await quiet(page, 1200); }
    // a page load of a screen visited before: the visit again
    for (const k of Object.keys(SCREENS)) { runs.againLoad[k].push(await load(k)); await quiet(page, 400); }
  }
  const rows = [['first visit, page load', 'firstLoad'], ['first visit, pressed in the page', 'firstPress'], ['again, pressed in the page', 'againPress'], ['again, page load', 'againLoad']];
  console.log(`\n  ${'time to content (median)'.padEnd(34)}${Object.keys(SCREENS).map((k) => k.padStart(12)).join('')}`);
  for (const [label, key] of rows) {
    result.numbers[key] = Object.fromEntries(Object.keys(SCREENS).map((k) => [k, median(runs[key][k])]));
    console.log(`  ${label.padEnd(34)}${Object.keys(SCREENS).map((k) => ms(result.numbers[key][k]).padStart(12)).join('')}`);
  }
  result.runs = runs;
  writeFileSync(join(out, `fluid-${LABEL}.json`), JSON.stringify(result, null, 2));
  console.log(`  written scripts/dev/out/fluid-${LABEL}.json`);

  if (!MEASURE_ONLY) {
    const eventually = async (fn, n = 6000) => { const t = Date.now(); while (Date.now() - t < n) { if (await fn()) return true; await page.waitForTimeout(40); } return false; };
    const asMe = { 'content-type': 'application/json', 'x-csrf-token': 'mock+csrf/token=' };
    // every API request the page makes, with the priority the browser gave it and when it went out and came back
    const cdp = await context.newCDPSession(page);
    await cdp.send('Network.enable');
    const reqs = new Map();
    cdp.on('Network.requestWillBeSent', (e) => { if (/\/api\/v1\//.test(e.request.url)) reqs.set(e.requestId, { url: e.request.url.replace(BASE, ''), prio: e.request.initialPriority, start: e.timestamp, end: null }); });
    const ended = (e) => { const r = reqs.get(e.requestId); if (r) r.end = e.timestamp; };
    cdp.on('Network.loadingFinished', ended);
    cdp.on('Network.loadingFailed', ended);
    /** The most of these requests out at the same moment. */
    const peakOf = (list) => { const ev = list.flatMap((r) => [[r.start, 1], [r.end ?? Infinity, -1]]).sort((a, b) => a[0] - b[0] || a[1] - b[1]); let n = 0, top = 0; for (const [, d] of ev) { n += d; top = Math.max(top, n); } return top; };

    // ---- a page loaded again: drawn from what was kept, put right when Canvas answers -------------------
    console.log('\nkept between page loads');
    {
      await load('dashboard');
      await quiet(page, 2500); // (what it opened with, kept: the write waits a moment for the answers that land together)
      await mockConfig({ apiDelay: { '': 2500 } }); // every answer two and a half seconds away
      const t = await load('dashboard');
      const early = await page.evaluate(() => { const s = document.querySelector('#bcv-main > .bcv-screen'); if (s) s.dataset.mark = 'kept'; return { values: [...document.querySelectorAll('.bcv-stats .bcv-stat__value')].map((e) => e.textContent), skel: !!document.querySelector('#bcv-main .bcv-skel') }; });
      check(t !== null && t < 1500 && early.values.length === 6 && early.values.every((v) => /^\d+$/.test(v)), `the Dashboard loaded again is on screen in ${ms(t)}, every counter a number, with each of Canvas's answers still ${2500} ms away: ${early.values.join(' | ')}`);
      await page.waitForTimeout(3500);
      const later = await page.evaluate(() => ({ same: document.querySelector('#bcv-main > .bcv-screen')?.dataset.mark === 'kept', values: [...document.querySelectorAll('.bcv-stats .bcv-stat__value')].map((e) => e.textContent) }));
      check(later.same && later.values.join('|') === early.values.join('|'), `Canvas's answers, once in, are what was kept: the screen is left exactly as it is, not drawn again: ${JSON.stringify(later)}`);
      await mockConfig({ apiDelay: LATENCY });
      // a task of the student's own added behind the page's back (another device): Canvas's list now differs from the kept one
      await load('todo');
      await quiet(page, 2500);
      const title = `Kept-check task ${Date.now()}`;
      const due = new Date(); due.setHours(23, 0, 0, 0);
      const note = await fetch(`${BASE}/api/v1/planner_notes`, { method: 'POST', headers: asMe, body: JSON.stringify({ title, todo_date: due.toISOString() }) }).then((r) => r.text()).then((x) => JSON.parse(x.replace(/^while\(1\);/, ''))).catch(() => null);
      await mockConfig({ apiDelay: { '': 1500 } });
      await load('todo');
      const kept = await page.evaluate((x) => ({ has: document.getElementById('bcv-main').innerText.includes(x), rows: document.querySelectorAll('#bcv-main .bcv-cal__todo .bcv-tdrag').length }), title); // (2.99.112: To Do's rows beside the calendar)
      const landed = await eventually(() => page.evaluate((x) => document.getElementById('bcv-main').innerText.includes(x), title), 8000);
      const how = await page.evaluate(() => ({ still: document.querySelector('#bcv-main > .bcv-screen')?.classList.contains('bcv-screen--still'), skel: !!document.querySelector('#bcv-main .bcv-skel') }));
      check(!!note?.id && !kept.has && kept.rows > 0 && landed && how.still && !how.skel, `To Do loaded again shows the kept list at once (${kept.rows} rows), and the task added since joins it in place when Canvas answers — drawn again still, no skeleton: ${JSON.stringify({ ...kept, landed, ...how })}`);
      if (note?.id) await fetch(`${BASE}/api/v1/planner_notes/${note.id}`, { method: 'DELETE', headers: asMe }).catch(() => {});
      await mockConfig({ apiDelay: LATENCY });
      // a list whose lifetime in the memo has run out (five minutes in a course, say): drawn from the answer it had, and asked again
      const inApp = (fn, arg) => sw.evaluate(async ([base, src, a]) => { const [t] = await chrome.tabs.query({ url: `${base}/*` }); const [r] = await chrome.scripting.executeScript({ target: { tabId: t.id }, world: 'ISOLATED', func: (code, x) => { if (code === 'lapse') self.BCV.canvas.tune({ lifetimes: x }); return true; }, args: [src, a] }); return r?.result; }, [BASE, fn, arg]);
      await load('dashboard');
      await inApp('lapse', 0.001); // (every lifetime a thousandth as long: the next answers lapse at once)
      await press('dashboard'); // (the screen on show pressed again: read afresh, under the short lifetimes)
      await quiet(page, 1500);
      await press('grades'); // (somewhere else, and back)
      await quiet(page, 1500);
      await mockConfig({ apiDelay: { '': 2000 } });
      const lapsedT = await press('dashboard');
      const lapsedVals = await page.evaluate(() => [...document.querySelectorAll('.bcv-stats .bcv-stat__value')].map((e) => e.textContent));
      check(lapsedT !== null && lapsedT < 1000 && lapsedVals.length === 6 && lapsedVals.every((v) => /^\d+$/.test(v)), `the Dashboard pressed again once its answers have lapsed in the memo is drawn from them at once (${ms(lapsedT)}), Canvas two seconds away, and asked again behind it: ${lapsedVals.join(' | ')}`);
      await page.waitForTimeout(2500);
      await mockConfig({ apiDelay: LATENCY });
      await inApp('lapse', 1);
      const stored = await sw.evaluate(async (k) => { const r = (await self.BCV.api.storage.local.get(k))[k]; return r ? { uid: r.uid, keys: Object.keys(r.entries).sort(), bytes: JSON.stringify(r).length, prose: JSON.stringify(r).includes('"message"') } : null; }, `kept:answers:localhost:${PORT}`);
      check(!!stored && stored.uid === '7' && stored.keys.includes('planner:21') && stored.keys.includes('courses:all') && !stored.keys.some((k) => /^(assignment|submission|agroups|conv|page|disc):/.test(k)) && !stored.prose && stored.bytes < 768 * 1024, `what is kept is this user's lists, never an assignment, a submission, a message or an announcement's body, under its size: ${JSON.stringify(stored)}`);
    }

    // ---- reading ahead: at idle, two at a time at most, at low priority; under a pointer at rest ---------
    console.log('\nreading ahead');
    {
      await page.mouse.move(700, 20); // (the pointer out of the way, over the bar: nothing under it is read ahead)
      await forgetKept();
      reqs.clear();
      await load('dashboard');
      const settledAt = await page.evaluate(() => performance.timeOrigin + performance.now());
      // (the idle queue: every root screen, then each favourite course's home, one at a time — waited for, then a moment more)
      const lows = () => [...reqs.values()].filter((r) => r.prio === 'Low');
      await eventually(() => { const l = lows(); return l.some((r) => /\/conversations\?/.test(r.url)) && l.some((r) => /\/assignment_groups\?/.test(r.url)) && l.filter((r) => /\/courses\/10\d\/tabs/.test(r.url)).length >= 4; }, 20000);
      await page.waitForTimeout(1500);
      const all = [...reqs.values()];
      const low = lows();
      const lowPeak = peakOf(low);
      check(low.length >= 8 && low.some((r) => /\/conversations\?/.test(r.url)) && low.some((r) => /\/assignment_groups\?/.test(r.url)) && low.filter((r) => /\/courses\/10\d\/tabs/.test(r.url)).length >= 4, `at idle the Inbox's, the Grades' and each favourite course's first requests are made ahead, at the browser's low priority (${low.length} of ${all.length} requests)`);
      check(lowPeak <= 2, `never more than two of those out at once (peak ${lowPeak}), so the screen on show and Canvas's own limits always have room`);
      check(!low.some((r) => /front_page|\/pages\/|\/assignments\/\d|\/discussion_topics\/\d|\/quizzes\/\d|\/files\/\d/.test(r.url)), `nothing read ahead at idle is an item or a front page (Canvas can count a read as a view): ${low.filter((r) => /front_page|\/pages\//.test(r.url)).map((r) => r.url).join(', ') || 'none'}`);
      void settledAt;
      const fronts = () => [...reqs.values()].filter((r) => /\/courses\/101\/front_page/.test(r.url));
      const n0 = fronts().length;
      const box = await page.$eval('.bcv-fav[data-load="fav:101"]', (e) => { const b = e.getBoundingClientRect(); return { x: b.left + b.width / 2, y: b.top + b.height / 2 }; });
      await page.mouse.move(box.x, box.y);
      await page.waitForTimeout(25);
      await page.mouse.move(700, 600); // (a pass on the way elsewhere)
      await page.waitForTimeout(500);
      const passed = fronts().length - n0;
      await page.mouse.move(box.x, box.y);
      const rested = await eventually(() => fronts().length > n0, 2000);
      const f = fronts()[n0];
      check(passed === 0 && rested && f?.prio === 'Low', `a pointer passing over a course's row reads nothing; resting on it reads its home page ahead, as a warm-up (${passed} on the pass, then ${fronts().length - n0} at rest, ${f?.prio})`);
      await page.waitForTimeout(700); // (its answer in)
      await mockConfig({ apiDelay: { '': 1500 } }); // (anything the press still had to ask for would take a second and a half)
      await page.evaluate((x) => { for (const m of document.querySelectorAll('#bcv-main > *')) m.dataset.old = '1'; window.__bcvProbe.arm(x); }, SCREENS.course.sel);
      const before = reqs.size;
      await page.mouse.down();
      await page.mouse.up();
      await page.waitForFunction(() => window.__bcvProbe.hit !== null, null, { timeout: 10000 }).catch(() => {});
      const hit = await page.evaluate(() => window.__bcvProbe.hit);
      const madeNow = [...reqs.values()].slice(before).filter((r) => /\/courses\/101\//.test(r.url) && !/assignments|agroups|assignment_groups/.test(r.url));
      check(hit !== null && hit < 700 && fronts().length === n0 + 1, `and the press lands its home from the memo in ${ms(hit)}, its front page not asked for again (${madeNow.map((r) => r.url.replace(/\?.*/, '')).join(', ') || 'nothing of the course asked for'})`);
      await settled(page);
      await mockConfig({ apiDelay: LATENCY });
    }

    // ---- the move between screens: one view transition, the way you went --------------------------------
    console.log('\nthe move between screens');
    {
      await load('dashboard');
      await quiet(page, 1500);
      // (headless Chromium draws in software, where the page keeps the transitions off — html.bcv-noblur — since taking the
      // picture a transition starts from costs a tenth of a second there: taken off here, as a desktop browser with
      // graphics acceleration has it)
      await page.evaluate(() => {
        document.documentElement.classList.remove('bcv-noblur');
        window.__vts = [];
        new MutationObserver(() => {
          const k = document.documentElement.dataset.bcvVt;
          if (!k) return;
          const rec = { k, scope: document.querySelector('[style*="view-transition-name"]')?.className || null, anims: new Set(), rootAnim: null };
          window.__vts.push(rec);
          const look = () => {
            for (const a of document.getAnimations()) {
              if (!/view-transition/.test(a.effect?.pseudoElement || '')) continue;
              rec.anims.add(`${a.animationName}@${a.effect.pseudoElement.replace(/^::view-transition-/, '')}`);
              if (/^bcv-vt-in/.test(a.animationName) && !rec.inKf) { const k = a.effect.getKeyframes(); rec.inKf = { from: k[0]?.transform, opacity: k[0]?.opacity, ms: a.effect.getTiming().duration, ease: k[0]?.easing, props: [...new Set(k.flatMap((f) => Object.keys(f)).filter((x) => !['offset', 'easing', 'composite', 'computedOffset'].includes(x)))].sort().join(',') }; }
            }
            const root = document.querySelector('#bcv-main > :not([data-old])');
            if (root && rec.rootAnim === null && rec.anims.size) rec.rootAnim = getComputedStyle(root).animationName;
            if (document.documentElement.dataset.bcvVt) requestAnimationFrame(look);
          };
          requestAnimationFrame(look);
        }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-bcv-vt'] });
      });
      const vts = () => page.evaluate(() => window.__vts.map((r) => ({ ...r, anims: [...r.anims].filter((a) => !/-ua-|plus-lighter/.test(a)).sort() })));
      await page.mouse.move(700, 20); // (the pointer out of the way, over the bar: nothing under it is read ahead)
      const settle = async () => { await settled(page); await page.waitForTimeout(500); };
      const go = async (sel) => { const n = (await vts()).length; await page.evaluate(() => { for (const m of document.querySelectorAll('#bcv-main > *')) m.dataset.old = '1'; }); await page.click(sel); await settle(); return (await vts())[n] || null; };
      await motionAt(page, 1); // (the transitions are watched as they play)
      const sec = await go('.bcv-nav__item[data-nav="calendar"]');
      check(sec?.k === 'fade' && sec.anims.join(',') === 'bcv-fade-in@new(bcv-screen),bcv-fade-out@old(bcv-screen)' && sec.rootAnim === 'none', `a section from the sidebar crossfades as one view transition of the main column, the screen's own entrance standing down: ${JSON.stringify(sec)}`);
      await go('.bcv-fav[data-load="fav:101"]');
      const tab = await go('.bcv-rail [data-tab="assignments"]');
      check(tab?.k === 'fade' && /bcv-cmain/.test(tab.scope || ''), `a course's own tab crossfades its column alone, the header and the rail holding still: ${JSON.stringify(tab)}`);
      const deeper = await go('#bcv-main .bcv-cmain a.bcv-row[href*="/assignments/"], #bcv-main .bcv-cmain .bcv-row');
      check(deeper?.k === 'fwd' && deeper.anims.join(',') === 'bcv-vt-in-fwd@new(bcv-screen),bcv-vt-out-fwd@old(bcv-screen)', `going deeper slides the new screen in from the right as the old one gives way to the left: ${JSON.stringify(deeper)}`);
      const n = (await vts()).length;
      await page.goBack();
      await settle();
      const back = (await vts())[n];
      check(back?.k === 'back' && back.anims.join(',') === 'bcv-vt-in-back@new(bcv-screen),bcv-vt-out-back@old(bcv-screen)', `Back reverses it: in from the left: ${JSON.stringify(back)}`);
      const kf = deeper?.inKf || {};
      check(kf.from === 'translateX(32px)' && kf.opacity === '0' && kf.ms === 220 && kf.ease === 'cubic-bezier(0.23, 1, 0.32, 1)' && kf.props === 'opacity,transform', `the move is 32px over 220ms on cubic-bezier(.23, 1, .32, 1), transform and opacity only: ${JSON.stringify(kf)}`);
      // a press while a transition plays: the transition ends at once and the press is answered
      await go('#bcv-main .bcv-cmain .bcv-row'); // (the item in the memo now, its back too)
      await page.goBack();
      await settle();
      await page.evaluate(() => { for (const m of document.querySelectorAll('#bcv-main > *')) m.dataset.old = '1'; });
      await page.click('#bcv-main .bcv-cmain .bcv-row', { noWaitAfter: true });
      const playing = await eventually(() => page.evaluate(() => document.getAnimations().some((a) => /view-transition/.test(a.effect?.pseudoElement || '') && a.playState === 'running')), 3000);
      const mod = await page.$eval('.bcv-rail [data-tab="modules"]', (e) => { const b = e.getBoundingClientRect(); return { x: b.left + b.width / 2, y: b.top + b.height / 2 }; });
      await page.mouse.click(mod.x, mod.y);
      const answered = await eventually(() => page.evaluate(() => /\/modules$/.test(location.pathname)), 5000);
      check(playing && answered, `a press while a transition plays finishes it and is answered (the rail's Modules, pressed mid-move: ${answered ? 'opened' : 'lost'})`);
      await settle();
      // reduced motion: no transition at all; the arrival is the 150ms fade every screen has
      await page.emulateMedia({ reducedMotion: 'reduce' });
      const before = (await vts()).length;
      await page.evaluate(() => { for (const m of document.querySelectorAll('#bcv-main > *')) m.dataset.old = '1'; });
      await page.click('.bcv-nav__item[data-nav="gpa"]');
      await settled(page);
      const reducedRoot = await page.evaluate(() => { const e = document.querySelector('#bcv-main > :not([data-old])'); const cs = getComputedStyle(e); return `${cs.animationName}@${cs.animationDuration}`; });
      check((await vts()).length === before && reducedRoot === 'bcv-fade-in@0.15s', `under reduced motion there is no transition: the screen fades in over 150 ms, nothing moving (${reducedRoot})`);
      await page.emulateMedia({ reducedMotion: 'no-preference' });
      // drawn in software (html.bcv-noblur): the screen's own entrance plays the same move, no transition
      await page.evaluate(() => document.documentElement.classList.add('bcv-noblur'));
      const before2 = (await vts()).length;
      await page.evaluate(() => { for (const m of document.querySelectorAll('#bcv-main > *')) m.dataset.old = '1'; });
      await page.click('.bcv-fav[data-load="fav:101"]');
      await settled(page);
      await page.evaluate(() => { for (const m of document.querySelectorAll('#bcv-main > *')) m.dataset.old = '1'; });
      await page.click('#bcv-main .bcv-cmain a[href*="/pages/"], #bcv-main .bcv-cmain a[href*="/assignments/"], #bcv-main .bcv-cmain .bcv-row');
      await settled(page);
      const softRoot = await page.evaluate(() => { const e = document.querySelector('#bcv-main > .bcv-screen'); const cs = getComputedStyle(e); return { name: cs.animationName, dur: cs.animationDuration, ease: cs.animationTimingFunction, arrive: e.dataset.bcvArrive }; });
      check((await vts()).length === before2 && ['bcv-arrive-fwd', 'bcv-fade-in'].includes(softRoot.name) && softRoot.dur === '0.22s' && /^cubic-bezier\(0\.23, 1, 0\.32, 1\)$/.test(softRoot.ease), `without view transitions the screen's own entrance plays the move, the same 220ms and curve: ${JSON.stringify(softRoot)}`);
      await motionAt(page, MOTION_RATE);
    }

    // ---- a real page load: its document fetched ahead, and crossfaded ------------------------------------
    console.log('\na real page load');
    {
      // a page Canvas draws itself (punched through): from it, every move to a screen of the interface is a real load —
      // drawn with graphics acceleration, as a desktop Chrome is (the site's own note of it, read as the page starts)
      await page.goto(`${BASE}/profile`);
      await page.evaluate(() => localStorage.setItem('bcv:blur', JSON.stringify({ drawn: true, at: Date.now() })));
      await page.goto(`${BASE}/profile`);
      await settled(page);
      const row = async (key) => page.$eval(`.bcv-nav__item[data-nav="${key}"]`, (e) => { const b = e.getBoundingClientRect(); return { x: b.left + b.width / 2, y: b.top + b.height / 2 }; });
      const cal = await row('calendar');
      await page.mouse.move(cal.x, cal.y);
      const asked = await eventually(() => page.evaluate(() => [...document.querySelectorAll('script[type="speculationrules"]')].some((x) => x.textContent.includes('"/calendar"'))), 2000);
      await page.waitForTimeout(600); // (the document fetched)
      await page.mouse.down();
      await page.mouse.up();
      await page.waitForURL(/\/calendar$/, { timeout: 10000 }).catch(() => {});
      await settled(page);
      const arrived = await page.evaluate(() => ({ delivery: performance.getEntriesByType('navigation')[0]?.deliveryType || '', reveal: window.__bcvReveal || null, noblur: document.documentElement.classList.contains('bcv-noblur') }));
      check(asked && arrived.delivery === 'navigational-prefetch', `from a page Canvas draws, a pointer resting on a sidebar row has Chrome fetch that page ahead (a speculation rule), and the press loads it from there: ${JSON.stringify({ asked, delivery: arrived.delivery })}`);
      check(arrived.reveal?.vt === true && arrived.reveal?.ran === true, `and the page arrives on a cross-document view transition, the page left behind fading out over it once it is drawn: ${JSON.stringify(arrived)}`);
      const items = await page.evaluate(() => [...document.querySelectorAll('script[type="speculationrules"]')].length);
      check(items === 0, `a screen of the interface asks for no document ahead (its moves stay on the page): ${items} rules`);
      // under reduced motion: no transition
      await page.emulateMedia({ reducedMotion: 'reduce' });
      await page.goto(`${BASE}/profile`);
      await settled(page);
      const dash = await row('dashboard');
      await page.mouse.click(dash.x, dash.y);
      await page.waitForURL(`${BASE}/`, { timeout: 10000 }).catch(() => {});
      await settled(page);
      const still = await page.evaluate(() => window.__bcvReveal || null);
      check(still && still.vt === false, `under reduced motion a page load has no transition: ${JSON.stringify(still)}`);
      await page.emulateMedia({ reducedMotion: 'no-preference' });
      await page.evaluate(() => localStorage.removeItem('bcv:blur'));
    }
  }
} catch (e) {
  console.log(`  crashed: ${e.stack || e.message}`);
  failures.push(`crash: ${e.message}`);
} finally {
  await context.close().catch(() => {});
  server.kill();
  rmSync(extDir, { recursive: true, force: true });
  rmSync(userDataDir, { recursive: true, force: true });
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n${failures.map((f) => ` - ${f}`).join('\n')}` : '\nfluid: all checks passed');
console.log(`(${((Date.now() - t0) / 1000).toFixed(1)}s)`);
process.exit(failures.length ? 1 : 0);


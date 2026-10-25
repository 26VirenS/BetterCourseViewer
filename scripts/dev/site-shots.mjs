#!/usr/bin/env node
// The website's pictures of the interface (site/public/img/): the extension in Chromium at 2×, on the mock Canvas and
// the mock Brightspace, signed in as their made-up students at a made-up school ("Lakeside University", a plain crest
// for its logo), in light and in dark. Each picture is written as a 2× WebP and a 1× WebP for the page's srcset.
//   node scripts/dev/site-shots.mjs [out-dir]        (default: site/public/img)
//   SITE_SHOTS=search,phone,tools node scripts/dev/site-shots.mjs   (only those; the names below, without web- or -dark)
// Requires Playwright (project or global install) and Python's Pillow (WebP).
import { createRequire } from 'node:module';
import { spawn, execSync, execFileSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync, rmSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import { launchExtension, afterMigration } from './harness.mjs';

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const outDir = process.argv[2] || join(root, 'site', 'public', 'img');
const raw = join(tmpdir(), `bcv-site-shots-${process.pid}`);
mkdirSync(outDir, { recursive: true });
mkdirSync(raw, { recursive: true });
const PORT = 8940, SIM = 8941, D2L = 8942;
const BASE = `http://localhost:${PORT}`, D2L_BASE = `http://localhost:${D2L}`;

// the school's crest: a shield with an open book, in Lakeside's navy and gold (made up, like the school)
const crest = `data:image/svg+xml,${encodeURIComponent(`<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96"><rect width="96" height="96" rx="22" fill="#12355b"/><path d="M48 16l24 9v20c0 16-10 27-24 33-14-6-24-17-24-33V25z" fill="#f2b632"/><path d="M34 40c5-2 10-2 14 1 4-3 9-3 14-1v20c-5-2-10-2-14 1-4-3-9-3-14-1z" fill="#12355b"/><path d="M48 41v20" stroke="#f2b632" stroke-width="2"/></svg>`)}`;

const extDir = join(tmpdir(), `bcv-site-ext-${process.pid}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*')) cs.matches.push(`${BASE}/*`, `${D2L_BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`, `${D2L_BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));

const canvas = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT), String(SIM)], { stdio: 'ignore' });
const d2l = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-brightspace.mjs'), String(D2L), '--open'], { stdio: 'ignore' });
process.on('exit', () => { canvas.kill(); d2l.kill(); });
for (const u of [`${BASE}/`, `${D2L_BASE}/d2l/login`]) for (let i = 0; i < 60; i++) { try { await fetch(u); break; } catch { await new Promise((r) => setTimeout(r, 100)); } }
await fetch(`${BASE}/__mock/config`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ richQuestions: true }) }).catch(() => {});

const userDataDir = join(tmpdir(), `bcv-site-profile-${process.pid}`);
const { context, sw } = await launchExtension(chromium, userDataDir, extDir, { viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 });
const made = [];
try {
  await afterMigration(sw);
  for (const p of context.pages()) if (p.url().includes('/setup/')) await p.close().catch(() => {});
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'welcome:grades': true, 'tips:rubricRing': true, 'tools:welcomed': true, 'whatsnew:seen': v }), manifest.version);
  // a term's grade history, as a student who turned tracking on in August would have it (the Grades page's trend)
  await sw.evaluate(async (k) => {
    const p = (await chrome.storage.local.get(k))[k] || {};
    p.gpaTracking = { since: '2026-08-24', priorGpa: 3.36, priorCourses: 8 };
    p.gpaGoal = 3.5;
    p.gpaSnapshots = [['2026-08-24', 3.18], ['2026-08-31', 3.27], ['2026-09-07', 3.24], ['2026-09-14', 3.33], ['2026-09-21', 3.31], ['2026-09-28', 3.38], ['2026-10-05', 3.42]].map(([date, gpa]) => ({ date, gpa }));
    await chrome.storage.local.set({ [k]: p });
  }, `prefs:localhost:${PORT}`);
  const settings = (patch) => sw.evaluate(async (p) => self.BCV.settings.update(p), patch);
  await settings({ appearance: { siteName: 'Lakeside University', logoUrl: crest, darkMode: 'off', sidebar: 'one' } }); // (the one sidebar, as a new install has it)
  // (the mock's student signs in with a real school's address; the pictures are of a made-up one, so every answer the
  //  interface reads says Lakeside's instead)
  await context.route(/^http:\/\/localhost:\d+\/(api|d2l\/api)\//, async (route) => {
    const res = await route.fetch();
    const type = res.headers()['content-type'] || '';
    if (!/json|text/.test(type)) return route.fulfill({ response: res });
    route.fulfill({ response: res, body: (await res.text()).replaceAll('ucmerced.edu', 'lakeside.edu').replaceAll('UC Merced', 'Lakeside University').replaceAll('Yue Lei', 'Dana Reyes') });
  });
  const page = await context.newPage();
  const settle = async () => {
    await page.waitForFunction(() => { const c = document.documentElement.classList; return c.contains('bcv-settled'); }, null, { timeout: 20000 }).catch(() => {});
    await page.waitForFunction(() => !document.querySelector('[data-rolling]'), null, { timeout: 6000 }).catch(() => {});
    await page.waitForTimeout(900);
    await page.mouse.move(1430, 890);
    await page.waitForTimeout(300);
  };
  const shot = async (name) => { await settle(); const f = join(raw, `${name}.png`); await page.screenshot({ path: f }); made.push(name); console.log('  shot', name); };
  const look = async (dark) => { await settings({ appearance: { darkMode: dark ? 'on' : 'off' } }); await page.waitForTimeout(250); };
  // (SITE_SHOTS: a comma list of the pictures to make — dashboard, grades, course, calendar, quiz, brightspace, search,
  //  phone, tools, canvas — else every one)
  const only = new Set(String(process.env.SITE_SHOTS || '').split(',').map((x) => x.trim()).filter(Boolean));
  const want = (name) => !only.size || only.has(name);

  for (const dark of [false, true]) {
    const sfx = dark ? '-dark' : '';
    await look(dark);
    if (want('dashboard')) {
      await page.goto(`${BASE}/`);
      await page.waitForSelector('.bcv-stat', { timeout: 20000 });
      await shot(`web-dashboard${sfx}`);
    }
    if (want('grades')) {
      await page.goto(`${BASE}/grades`);
      await page.waitForSelector('.bcv-gpa__value', { timeout: 20000 });
      await shot(`web-grades${sfx}`);
    }
    if (want('course')) {
      await page.goto(`${BASE}/courses/101`);
      await page.waitForSelector('.bcv-cmain, .bcv-course', { timeout: 20000 }).catch(() => {});
      await shot(`web-course${sfx}`);
    }
    if (want('calendar')) {
      await page.goto(`${BASE}/calendar`);
      await page.waitForTimeout(600);
      await shot(`web-calendar${sfx}`);
    }
    if (want('quiz')) {
      // a quiz, a question at a time: begun, the first answer picked
      await page.goto(`${BASE}/courses/101/quizzes/9011`);
      await page.waitForSelector('.bcv-qz__begin', { timeout: 20000 }); // (the quiz opens on its intro)
      await page.click('.bcv-qz__begin');
      await page.waitForSelector('.bcv-qz__opt', { timeout: 20000 });
      await page.waitForTimeout(900);
      await page.click('.bcv-qz__opt >> nth=3');
      await page.waitForSelector('.bcv-qz__opt.is-selected', { timeout: 8000 }).catch(() => {});
      await shot(`web-quiz${sfx}`);
    }
    if (want('brightspace')) {
      // and Brightspace's: the same interface over Lakeside's Brightspace
      await page.goto(`${D2L_BASE}/d2l/home`);
      await page.waitForSelector('.bcv-stat', { timeout: 20000 }).catch(() => {});
      await shot(`web-brightspace${sfx}`);
    }
    if (want('search')) {
      // one search across every course: a word typed, its results under the box by kind
      await page.goto(`${BASE}/`);
      await page.waitForSelector('#bcv-omni', { timeout: 20000 });
      await settle();
      await page.click('#bcv-omni');
      await page.keyboard.type('lab', { delay: 60 });
      await page.waitForSelector('#bcv-omni-panel [role="option"]', { timeout: 15000 }).catch(() => {});
      await page.waitForTimeout(1200);
      await shot(`web-search${sfx}`);
      await page.keyboard.press('Escape');
    }
    if (want('phone')) {
      // hand in from your phone: the assignment's code for the phone's camera (Week 2, which takes a file)
      await page.goto(`${BASE}/courses/104/assignments/4002?bcv=submit`);
      await page.waitForSelector('.bcv-sb__phonego', { timeout: 20000 });
      await settle();
      await page.click('.bcv-sb__phonego');
      await page.waitForSelector('.bcv-phone-ov .bcv-phone__row', { timeout: 15000 });
      await page.waitForTimeout(700);
      await shot(`web-handin-phone${sfx}`);
      await page.keyboard.press('Escape');
    }
    if (want('tools')) {
      // the tools: their page, then the periodic table opened over it
      await page.goto(`${BASE}/#tools`);
      await page.waitForSelector('.bcv-tool-card', { timeout: 20000 });
      await shot(`web-tools${sfx}`);
      await page.click('.bcv-tool-card[data-tool="ptable"]');
      await page.waitForSelector('[data-tool="ptable"].bcv-tool-sheet, .bcv-tool-sheet[data-tool="ptable"], [data-tool="ptable"]:not(.bcv-tool-card)', { timeout: 15000 }).catch(() => {});
      await page.waitForTimeout(1500);
      await shot(`web-ptable${sfx}`);
    }
  }
  // the same student's Dashboard as Canvas itself draws it, with no extension: the "before" under the hero's slider. The
  // mock's own page is a bare stand-in, so Canvas's stock Dashboard is drawn here — its dark global nav, the course cards,
  // To Do down the right — from the mock school's own courses and work (Canvas has one look, so one picture)
  const plain = want('canvas') ? await chromium.launch() : null;
  if (plain) try {
    const pp = await (await plain.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 })).newPage();
    await pp.setContent(canvasDashboard(crest));
    await pp.waitForTimeout(400);
    await pp.screenshot({ path: join(raw, 'web-canvas.png') });
    made.push('web-canvas');
    console.log('  shot web-canvas');
  } finally { await plain.close(); }
} catch (e) {
  console.error('crashed:', e?.stack || e);
  process.exitCode = 1;
} finally {
  await context.close().catch(() => {});
  rmSync(userDataDir, { recursive: true, force: true });
  rmSync(extDir, { recursive: true, force: true });
}

/** Canvas's stock Dashboard for Sam Student at Lakeside, as Canvas draws it (the "before" on the website). */
function canvasDashboard(logo) {
  const courses = [['F26-MATH 021 20', 'MATH-021-20', 'Fall 2026', '#0B874B'], ['F26-PHYS 008 01', 'PHYS-008-01', 'Fall 2026', '#0770A3'], ['F26-PHYS 008HL 01/PHYS 008L 01', 'PHYS-008HL', 'Fall 2026', '#BF32A4'], ['F26-SPRK 010 103', 'SPRK-010-103', 'Fall 2026', '#E71F63'], ['F26-WRI 010 20', 'WRI-010-20', 'Fall 2026', '#8F3E97'], ['F26-CHEM 002 01', 'CHEM-002-01', 'Fall 2026', '#4D3D4D']];
  const todo = [['Dis01', 'F26-MATH 021 20', '10 points • Oct 20 at 11:59pm'], ['Qz01', 'F26-MATH 021 20', '10 points • Oct 20 at 11:59pm'], ['Week 2 Post Class Assignment: GC articles', 'F26-SPRK 010 103', '10 points • Oct 20 at 11:59pm'], ['Research Day Activity: Choosing a Field Site', 'F26-WRI 010 20', '5 points • Oct 20 at 11:59pm'], ['Lec06-PreQuiz', 'F26-MATH 021 20', '17 points • Oct 21 at 10:30am'], ['Composition of Functions', 'F26-MATH 021 20', '30 points • Oct 21 at 11:59pm']];
  const nav = [['Account', '<circle cx="12" cy="9" r="4"/><path d="M4 21c1.5-4 4.5-6 8-6s6.5 2 8 6"/>'], ['Dashboard', '<path d="M4 13a8 8 0 1 1 16 0"/><path d="M12 13l4-4"/>'], ['Courses', '<path d="M5 4h10a3 3 0 0 1 3 3v13H8a3 3 0 0 1-3-3z"/><path d="M5 17a3 3 0 0 1 3-3h10"/>'], ['Groups', '<circle cx="8" cy="9" r="3"/><circle cx="16" cy="9" r="3"/><path d="M2 20c1-3 3.5-4.5 6-4.5s5 1.5 6 4.5M10 20c1-3 3.5-4.5 6-4.5s5 1.5 6 4.5"/>'], ['Calendar', '<rect x="4" y="5" width="16" height="15" rx="1"/><path d="M4 10h16M9 3v4M15 3v4"/>'], ['Inbox', '<path d="M3 13l3-8h12l3 8v6H3z"/><path d="M3 13h5l1 3h6l1-3h5"/>'], ['History', '<circle cx="12" cy="12" r="8"/><path d="M12 8v4l3 2"/>'], ['Help', '<circle cx="12" cy="12" r="8"/><path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.6.3-1 .8-1 1.5v.7M12 17h.01"/>']];
  const card = ([name, code, term, colour]) => `<div class="card"><div class="hero" style="background:${colour}"><span class="dots">⋮</span></div><div class="body"><div class="name" style="color:${colour}">${name}</div><div class="code">${code}</div><div class="term">${term}</div><div class="icons"><i></i><i></i><i></i><i></i></div></div></div>`;
  const item = ([t, c, d]) => `<li><span class="chk"></span><div><a>${t}</a><div class="ctx">${c}</div><div class="due">${d}</div></div><span class="x">×</span></li>`;
  return `<!doctype html><html><head><meta charset="utf-8"><style>
  * { box-sizing: border-box; } body { margin: 0; font: 16px/1.4 "Lato", "Helvetica Neue", Helvetica, Arial, sans-serif; color: #2d3b45; background: #fff; display: flex; height: 100vh; overflow: hidden; }
  .gnav { width: 84px; flex: none; background: #12355b; display: flex; flex-direction: column; align-items: center; padding-top: 10px; }
  .gnav .logo { width: 56px; height: 56px; margin-bottom: 12px; } .gnav .logo img { width: 100%; height: 100%; border-radius: 10px; }
  .gnav a { width: 84px; padding: 9px 0 8px; display: flex; flex-direction: column; align-items: center; gap: 3px; color: #fff; font-size: 12px; text-decoration: none; }
  .gnav a.on { background: #fff; color: #12355b; } .gnav svg { width: 26px; height: 26px; fill: none; stroke: currentColor; stroke-width: 1.6; stroke-linecap: round; stroke-linejoin: round; }
  main { flex: 1; display: flex; flex-direction: column; min-width: 0; }
  header { height: 72px; display: flex; align-items: center; justify-content: space-between; padding: 0 24px; border-bottom: 1px solid #c7cdd1; }
  header h1 { font-size: 28px; font-weight: 400; margin: 0; } header .opt { width: 38px; height: 38px; border: 1px solid #c7cdd1; border-radius: 4px; display: flex; align-items: center; justify-content: center; color: #2d3b45; font-size: 20px; }
  .wrap { flex: 1; display: flex; min-height: 0; } .grid { flex: 1; padding: 24px; display: flex; flex-wrap: wrap; gap: 36px 24px; align-content: flex-start; }
  .card { width: 262px; height: 266px; box-shadow: 0 2px 5px rgba(0,0,0,.3); border-radius: 4px; overflow: hidden; background: #fff; }
  .hero { height: 146px; position: relative; } .dots { position: absolute; top: 10px; right: 12px; color: #fff; font-size: 20px; }
  .body { padding: 12px 16px 0; } .name { font-weight: 700; font-size: 16px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; } .code, .term { font-size: 14px; color: #6b7780; white-space: nowrap; }
  .icons { display: flex; gap: 30px; margin-top: 14px; padding-left: 4px; } .icons i { width: 20px; height: 18px; border: 2px solid #6b7780; border-radius: 3px; opacity: .8; }
  aside { width: 312px; flex: none; padding: 22px 24px; } aside .btn { display: block; text-align: center; padding: 8px; border: 1px solid #c7cdd1; border-radius: 4px; background: #f5f5f5; font-size: 15px; margin-bottom: 10px; }
  aside h2 { font-size: 16px; font-weight: 700; margin: 22px 0 6px; padding-bottom: 6px; border-bottom: 1px solid #c7cdd1; }
  aside ul { list-style: none; margin: 0; padding: 0; } aside li { display: flex; gap: 10px; padding: 9px 0; font-size: 14px; border-bottom: 1px solid #eef0f1; }
  aside li .chk { width: 18px; height: 18px; border: 2px solid #8b969e; border-radius: 50%; flex: none; margin-top: 1px; } aside li > div { flex: 1; min-width: 0; }
  aside li a { color: #0770a3; font-weight: 700; } .ctx, .due { color: #6b7780; font-size: 12.5px; } .x { color: #8b969e; }
</style></head><body>
<nav class="gnav"><div class="logo"><img src="${logo}" alt=""></div>${nav.map(([n, d], i) => `<a class="${i === 1 ? 'on' : ''}"><svg viewBox="0 0 24 24">${d}</svg>${n}</a>`).join('')}</nav>
<main><header><h1>Dashboard</h1><span class="opt">⋮</span></header>
<div class="wrap"><div class="grid">${courses.map(card).join('')}</div>
<aside><span class="btn">Start a New Course</span><span class="btn">View Grades</span><h2>To Do</h2><ul>${todo.map(item).join('')}</ul></aside></div></main>
</body></html>`;
}

// 2× and 1× WebP of each, for srcset (Pillow)
if (made.length) {
  execFileSync('python3', ['-c', `
import sys
from PIL import Image
raw, out = sys.argv[1], sys.argv[2]
for name in sys.argv[3:]:
    im = Image.open(f"{raw}/{name}.png").convert("RGB")
    im.save(f"{out}/{name}@2x.webp", "WEBP", quality=82, method=6)
    im.resize((im.width // 2, im.height // 2), Image.LANCZOS).save(f"{out}/{name}.webp", "WEBP", quality=84, method=6)
    print("  webp", name, im.size)
`, raw, outDir, ...made], { stdio: 'inherit' });
}
console.log(`raw PNGs in ${raw}`);
process.exit(process.exitCode || 0);

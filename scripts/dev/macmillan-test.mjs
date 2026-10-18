#!/usr/bin/env node
// (Mac 1.3.114, Windows 0.3.32) Macmillan Learning's skin (extension/tools/macmillan/skin.js), checked against the mock's
// stand-in for Macmillan's assessment (scripts/dev/mock-macmillan.mjs), in Chromium as the apps run it — in every page
// and frame, its reports caught where the Mac's message handler would take them: the cover read (its name, due line,
// questions and grading settings), Open Assignment pressed for the app, the question's state (the list of questions,
// the score, the controls), an answer submitted and the question solved, a question opened from the list, Macmillan's
// own chrome put away while skinned and back with Show Browser, and no error on the page.
//   node scripts/dev/macmillan-test.mjs
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }
const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const PORT = 8841, SIM_PORT = 8842;
const skin = readFileSync(join(root, 'extension', 'tools', 'macmillan', 'skin.js'), 'utf8');

let passed = 0, failed = 0;
const check = (ok, what) => { if (ok) passed++; else { failed++; console.log('✗ ' + what); } };

const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT), String(SIM_PORT)], { stdio: 'ignore' });
const stop = () => { try { server.kill(); } catch { /* gone */ } };
process.on('exit', stop);
try {
  for (let i = 0; i < 50; i++) {
    try { const r = await fetch(`http://localhost:${PORT}/mock-macmillan/assignment/a1`); if (r.ok) break; } catch { /* not up yet */ }
    await new Promise((r) => setTimeout(r, 100));
  }
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1000, height: 720 } });
  await ctx.addInitScript(() => { window.__msgs = []; window.webkit = { messageHandlers: { simplMacmillan: { postMessage: (m) => window.__msgs.push(JSON.parse(m)) } } }; });
  await ctx.addInitScript(skin);
  const page = await ctx.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto(`http://localhost:${PORT}/mock-macmillan/assignment/a1`);
  const sac = () => page.frames().find((f) => /\/sac\/12778033/.test(f.url())); // (the player, posted into the hub's launch-frame; not its widget frame under /sac/static)
  const last = async () => (await sac().evaluate(() => window.__msgs.filter((m) => m.kind === 'state').slice(-1)[0] || null));
  const until = async (fn, ms = 4000) => { const end = Date.now() + ms; for (;;) { const v = await fn(); if (v || Date.now() > end) return v; await new Promise((r) => setTimeout(r, 80)); } };

  const cover = await until(async () => sac() && (await last())?.page === 'cover' && (await last()));
  check(cover && cover.cover.name === 'W8 Homework: Forces in Motion' && cover.cover.due === 'Due Mon, Oct 26, 11:59 pm' && cover.cover.count === 4 && cover.cover.policies.length === 5, `the cover is read: ${JSON.stringify(cover)}`);
  const framed = await page.evaluate(() => { const f = document.querySelector('iframe[data-simpl-sac]'); const r = f && f.getBoundingClientRect(); return !!r && r.top === 0 && r.left === 0 && r.width === innerWidth && r.height === innerHeight; });
  check(framed, 'the Integration Hub page gives the assessment the whole page');

  await sac().evaluate(() => window.__simplMacmillan('open'));
  const q1 = await until(async () => (await last())?.page === 'question' && (await last()));
  check(q1 && q1.title === 'W8 Homework: Forces in Motion' && q1.index === 0 && q1.count === 4 && q1.completed === 0 && JSON.stringify(q1.score) === '[0,400]' && q1.items.length === 4 && q1.items.every((i) => i.state === 'default' && i.note === 'Unlimited attempts') && q1.main === 'Submit Answer' && q1.tools.resources && q1.tools.tutor && !q1.tools.solution && q1.tools.layout && q1.policies.length === 5, `Open Assignment, then the first question's state: ${JSON.stringify(q1)}`);
  const hidden = await sac().evaluate(() => ['#check-bar', '#attempts-holder', '#question-nav'].map((s) => { const e = document.querySelector(s); return e ? getComputedStyle(e).display === 'none' || !e.offsetParent : true; }));
  check(hidden.every(Boolean), `Macmillan's own chrome is put away while skinned: ${JSON.stringify(hidden)}`);

  await sac().fill('input[data-i="2"]', '2.5');
  await sac().fill('input[data-i="3"]', '5');
  await sac().evaluate(() => window.__simplMacmillan('main'));
  const solved = await until(async () => { const s = await last(); return s?.completed === 1 && s; });
  check(solved && solved.items[0].state === 'success' && solved.items[0].note === 'Solved in 1 attempt' && solved.items[0].points === '+100' && JSON.stringify(solved.score) === '[100,400]' && solved.main === 'Next Question' && solved.tools.solution && solved.attempts && solved.attempts.choices.join('|') === 'Question|My Answer' && solved.results.join('|') === 'Correct|Correct', `the answer submitted, the question solved: ${JSON.stringify(solved)}`);

  await sac().evaluate(() => window.__simplMacmillan('go', 3));
  const q3 = await until(async () => { const s = await last(); return s?.index === 2 && s; });
  check(q3 && q3.main === 'Submit Answer' && q3.items[0].state === 'success', 'a question opened from the list');

  await sac().evaluate(() => window.__simplMacmillan('skin', false));
  const shown = await until(async () => sac().evaluate(() => !!document.querySelector('#check-bar') && getComputedStyle(document.querySelector('#check-bar')).display !== 'none'));
  check(shown, "Show Browser: Macmillan's page as it draws it");

  // (Mac 1.3.115) a frame of the player's own (a widget, under /sac/ too) says nothing: its blank page never stands in for the question
  const widget = page.frames().find((f) => /\/sac\/static\//.test(f.url()));
  check(!!widget && (await widget.evaluate(() => window.__msgs.filter((m) => m.kind === 'state').length)) === 0, "the player's own widget frame reports nothing");

  // (Mac 1.3.115) Macmillan's side panel left folded: folded, it draws neither the list nor the grading settings — skinned, it
  // is unfolded (hidden still) so they are read; and the hub's Assessment bar is put away under the player
  await page.goto(`http://localhost:${PORT}/mock-macmillan/assignment/a1?folded=1`);
  await until(async () => sac() && (await last())?.page === 'cover');
  await sac().evaluate(() => window.__simplMacmillan('open'));
  const folded = await until(async () => { const st = await last(); return st?.page === 'question' && st.items.length === 4 && st.policies.length === 5 && st; }, 5000);
  check(!!folded && folded.completed === 0 && folded.count === 4, `a folded side panel is unfolded for the list and the grading settings: ${JSON.stringify(folded && { items: folded.items.length, policies: folded.policies.length, completed: folded.completed })}`);
  const hubBar = await page.evaluate(() => { const f = document.querySelector('iframe[name="launch-frame"]'); const r = f.getBoundingClientRect(); return { marked: f.hasAttribute('data-simpl-sac'), full: r.top === 0 && r.height === innerHeight }; });
  check(hubBar.marked && hubBar.full, `the hub's launch-frame, posted into with no /sac/ address of its own, is made the whole page: ${JSON.stringify(hubBar)}`);

  check(errors.length === 0, `no error on the page: ${errors.join(' | ')}`);
  await browser.close();
} finally {
  stop();
}
console.log(`macmillan: ${passed} passed, ${failed} failed`);
process.exit(failed ? 1 : 0);

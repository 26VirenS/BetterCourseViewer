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

  // (Mac 1.3.116) as wide as the popup and never wider — Macmillan's 960-px sheet, set beside a side panel's 300 px kept
  // with the panel put away, ran under the edge and cut off the two-column question's picture on the right
  const fit = async () => sac().evaluate(() => {
    const main = document.querySelector('#main'), sheet = document.querySelector('.content-area');
    const cards = [...document.querySelectorAll('article.module-container')];
    const img = document.querySelector('.raw-html img'), text = cards[0].getBoundingClientRect(), pic = img.getBoundingClientRect();
    return { over: main.scrollWidth - main.clientWidth, sheetRight: sheet.getBoundingClientRect().right, width: innerWidth, sheetWidth: sheet.getBoundingClientRect().width,
      pic: { left: pic.left, right: pic.right, top: pic.top, w: pic.width, h: pic.height }, text: { left: text.left, right: text.right, top: text.top, bottom: text.bottom },
      ground: getComputedStyle(document.body).backgroundColor, ink: getComputedStyle(cards[0]).color, plate: getComputedStyle(img).backgroundColor, dark: document.documentElement.classList.contains('simpl-mm-dark') };
  });
  await page.setViewportSize({ width: 820, height: 720 }); // (the page's width in the popup, beside the app's rail)
  const wide = await until(async () => { const f = await fit(); return f.width === 820 && f; });
  check(wide.over <= 1 && wide.sheetRight <= wide.width + 1 && wide.sheetWidth >= wide.width - 80, `the question fills the width and no more: ${JSON.stringify(wide)}`);
  check(wide.pic.w > 100 && wide.pic.h > 60 && wide.pic.right <= wide.width && wide.pic.left >= wide.text.right && Math.abs(wide.pic.top - wide.text.top) < 40, `the two-column question's picture in sight, beside the words: ${JSON.stringify({ pic: wide.pic, text: wide.text })}`);
  const panel = await sac().evaluate(() => ({ panel: getComputedStyle(document.querySelector('.parent-module-container')).backgroundColor, part: getComputedStyle(document.querySelector('article.module-container')).backgroundColor }));
  check(panel.panel !== 'rgba(0, 0, 0, 0)' && panel.part === 'rgba(0, 0, 0, 0)', `the whole question one panel, its parts not cards of their own: ${JSON.stringify(panel)}`);
  check(wide.ground === 'rgba(0, 0, 0, 0)' && !wide.dark && wide.ink === 'rgb(28, 28, 30)', `laid on the popup's glass, light: ${JSON.stringify({ ground: wide.ground, ink: wide.ink })}`);
  await page.setViewportSize({ width: 560, height: 720 });
  const narrow = await until(async () => { const f = await fit(); return f.width === 560 && f; });
  check(narrow && narrow.over <= 1 && narrow.pic.top >= narrow.text.bottom - 1 && narrow.pic.right <= 560, `narrow, one column, the picture under the words: ${JSON.stringify(narrow && { over: narrow.over, pic: narrow.pic, text: narrow.text })}`);
  // (Mac 1.3.122) a narrower window, still two columns: the picture a smaller preview, wholly in sight
  await page.setViewportSize({ width: 680, height: 720 });
  const mid = await until(async () => { const f = await fit(); return f.width === 680 && f; });
  check(mid && mid.pic.right <= 680 - 8 && mid.pic.h <= 220 && mid.pic.left >= mid.text.right, `a narrower window: the picture smaller, beside the words, never past the edge: ${JSON.stringify(mid && mid.pic)}`);
  await page.setViewportSize({ width: 820, height: 720 });
  await sac().evaluate(() => window.__simplMacmillan('theme', true));
  const dk = await until(async () => { const f = await fit(); return f.dark && f; });
  const dkState = await until(async () => { const s = await last(); return s?.dark === true && s; });
  check(dk && dk.ink === 'rgb(242, 242, 247)' && dk.plate === 'rgb(255, 255, 255)' && !!dkState, `dark as the app is: light words, the picture on a white plate: ${JSON.stringify(dk && { ink: dk.ink, plate: dk.plate })}`);
  await sac().evaluate(() => window.__simplMacmillan('theme', false));
  await page.setViewportSize({ width: 1000, height: 720 });

  await sac().fill('input[data-i="2"]', '2.5');
  await sac().fill('input[data-i="3"]', '5');
  await sac().evaluate(() => window.__simplMacmillan('main'));
  const solved = await until(async () => { const s = await last(); return s?.completed === 1 && s; });
  check(solved && solved.items[0].state === 'success' && solved.items[0].note === 'Solved in 1 attempt' && solved.items[0].points === '+100' && JSON.stringify(solved.score) === '[100,400]' && solved.main === 'Next Question' && solved.tools.solution && solved.attempts && solved.attempts.choices.join('|') === 'Question|My Answer' && solved.results.join('|') === 'Correct|Correct', `the answer submitted, the question solved: ${JSON.stringify(solved)}`);

  // (Mac 1.3.117) the verdict is the app's pill: Macmillan's Solved bar put away, its word reported; each answer's own
  // Correct a pill that reads (its ink not the page's)
  const pill = await sac().evaluate(() => { const g = document.querySelector('.grading-status'), r = document.querySelector('#results'); const cs = getComputedStyle(g); return { bar: r ? getComputedStyle(r).display : 'none', ink: cs.color, bg: cs.backgroundColor, tick: getComputedStyle(g, '::before').content }; });
  check(solved && solved.verdict === 'right' && pill.bar === 'none' && pill.ink === 'rgb(26, 107, 48)' && pill.tick.includes('✓'), `the verdict reported, Macmillan's bar put away, the answer's Correct a pill: ${JSON.stringify({ verdict: solved && solved.verdict, ...pill })}`);
  await sac().evaluate(() => window.__simplMacmillan('theme', true));
  const pillDark = await until(async () => sac().evaluate(() => document.documentElement.classList.contains('simpl-mm-dark') && getComputedStyle(document.querySelector('.grading-status')).color));
  check(pillDark === 'rgb(93, 219, 125)', `in dark the answer's Correct keeps its green ink: ${pillDark}`);
  await sac().evaluate(() => window.__simplMacmillan('theme', false));

  await sac().evaluate(() => window.__simplMacmillan('go', 3));
  const q3 = await until(async () => { const s = await last(); return s?.index === 2 && s; });
  check(q3 && q3.main === 'Submit Answer' && q3.items[0].state === 'success', 'a question opened from the list');

  // (Mac 1.3.119) a one-column question with a picture: the picture up the right, the question and its answers down the
  // left in their order
  await page.setViewportSize({ width: 820, height: 720 });
  await sac().evaluate(() => window.__simplMacmillan('go', 2));
  await until(async () => (await last())?.index === 1);
  const q2 = await until(async () => sac().evaluate(() => {
    const pic = document.querySelector('[data-simpl-pic]'); if (!pic) return null;
    const left = [...document.querySelectorAll('article.module-container:not([data-simpl-pic])')].map((e) => e.getBoundingClientRect());
    const p = pic.getBoundingClientRect();
    return { picLeft: Math.round(p.left), picTop: Math.round(p.top), leftRight: Math.round(Math.max(...left.map((r) => r.right))), lefts: [...new Set(left.map((r) => Math.round(r.left)))], tops: left.map((r) => Math.round(r.top)), right: Math.round(p.right), width: innerWidth };
  }));
  check(q2 && q2.picLeft >= q2.leftRight && q2.lefts.length === 1 && q2.tops.every((t, i, a) => i === 0 || t > a[i - 1]) && q2.right <= q2.width && Math.abs(q2.picTop - q2.tops[0]) < 60, `a picture up the right, the question then its answers down the left: ${JSON.stringify(q2)}`);
  await page.setViewportSize({ width: 1000, height: 720 });

  // (Mac 1.3.120) a number's unit beside its field, not across the page; a picture pressed shows large, Escape puts it away
  await sac().evaluate(() => window.__simplMacmillan('go', 1));
  await until(async () => (await last())?.index === 0);
  const unit = await sac().evaluate(() => { const f = document.querySelector('.numeric-entry-border').getBoundingClientRect(), u = [...document.querySelectorAll('.numeric-entry-table .graded-equations-td')].pop().getBoundingClientRect(); return Math.round(u.left - f.right); });
  check(unit >= 0 && unit < 40, `the unit sits beside its field: ${unit}px away`);
  await sac().click('[data-simpl-pic] img');
  const zoomed = await until(async () => sac().evaluate(() => { const z = document.getElementById('simpl-mm-zoom'); return z && z.querySelector('img').getBoundingClientRect().width > 300; }));
  await sac().evaluate(() => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true })));
  check(zoomed && (await sac().evaluate(() => !document.getElementById('simpl-mm-zoom'))), 'a picture pressed shows large over the page, and Escape puts it away');
  // (Mac 1.3.123) Macmillan's pop-up (You solved it…) as the app's own card, as Knewton Alta's are: read whole, hidden in the
  // page while the app shows it, its buttons pressed from the app; given back, it shows as drawn; gone, the app is told
  const dlg = await sac().evaluate(async () => {
    window.__simplMacmillanGlass = true;
    const wrap = document.createElement('div');
    wrap.innerHTML = '<div style="position: fixed; top: 0px; left: 0px; width: 100%; height: 100%; z-index: 1050; background: rgba(0, 0, 0, 0.5);"><div role="dialog" id="react-aria-modal-dialog" class="sac-modal" aria-labelledby="modal-title"><div class="modal-solution-container"><div class="modal-title-container-borderless"><h1 id="modal-title">  </h1><button data-test-id="close-modal-x" id="close-modal-x" aria-label="Close Modal"><svg width="15" height="15" viewBox="0 0 24 24"><polygon points="1 1 2 2"></polygon></svg><span class="sr-only">Close modal</span></button></div><div id="solution-modal-content" class="content solution"><svg width="200" height="152" viewBox="0 0 200 152"><circle cx="100" cy="76" r="50" fill="#d8f1f5"></circle></svg><p data-test-id="modal-body-text" class="message attempts solution">You solved it  in 1 attempt!</p></div><div class="modal-actions" data-test-id="modal-actions"><button id="solution-modal-view-solution" class="modal-button cdl-button secondary">View Solution</button><button id="solution-modal-next-item" class="modal-button cdl-button primary">Next Question</button></div></div></div></div>';
    document.body.appendChild(wrap);
    window.__pressed = [];
    wrap.querySelectorAll('button').forEach((b) => b.addEventListener('click', () => window.__pressed.push(b.id)));
    const posted = () => window.__msgs.filter((m) => m.kind === 'dialog').slice(-1)[0];
    for (let i = 0; i < 40 && !(posted() && posted().open); i++) await new Promise((r) => setTimeout(r, 50));
    const m = posted();
    const hidden = getComputedStyle(wrap.firstElementChild).display === 'none';
    window.__simplMacmillan('dialog', 2);
    const pressed = window.__pressed.slice();
    window.__simplMacmillan('dialogPage');
    const shownAgain = getComputedStyle(wrap.firstElementChild).display !== 'none';
    wrap.remove();
    for (let i = 0; i < 40 && posted().open; i++) await new Promise((r) => setTimeout(r, 50));
    window.__simplMacmillanGlass = false;
    return { open: m && m.open, title: m && m.title, words: m && /You solved it\s+in 1 attempt!/.test(m.html), svg: m && /<svg/.test(m.html), buttons: m && m.buttons.map((b) => `${b.label}${b.primary ? '*' : ''}${b.close ? '(x)' : ''}`).join(' | '), hidden, pressed, shownAgain, closed: posted().open === false };
  });
  check(dlg.open && dlg.title === '' && dlg.words && dlg.svg && dlg.buttons === 'Close(x) | View Solution | Next Question*' && dlg.hidden && dlg.pressed.join() === 'solution-modal-next-item' && dlg.shownAgain && dlg.closed, `Macmillan's pop-up goes to the app whole — its words, its drawing, Close, View Solution and Next Question (the main one) — hidden in the page meanwhile; the app's press presses Macmillan's; given back it shows; gone, the app is told: ${JSON.stringify(dlg)}`);

  // (Mac 1.3.119) a part of Macmillan's that draws its own (a labelling with its answer bank): in dark kept as drawn on a
  // white plate, its words dark, and wider than the page it scrolls in its part rather than being cut
  await sac().evaluate(() => { window.__simplMacmillan('go', 3); window.__simplMacmillan('theme', true); });
  const plate = await until(async () => sac().evaluate(() => {
    const m = document.querySelector('[data-module-type="labeling"]')?.closest('article'); if (!m || !m.hasAttribute('data-simpl-plate') || !document.documentElement.classList.contains('simpl-mm-dark')) return null;
    return { bg: getComputedStyle(m).backgroundColor, chip: getComputedStyle(m.querySelector('.mm-chip')).color, scroll: getComputedStyle(m.querySelector('.sl-module')).overflowX };
  }));
  check(plate && plate.bg === 'rgb(255, 255, 255)' && plate.chip === 'rgb(34, 34, 34)' && plate.scroll === 'auto', `a widget of Macmillan's own on a white plate in dark, its words as drawn, scrolling in its part: ${JSON.stringify(plate)}`);
  await sac().evaluate(() => window.__simplMacmillan('theme', false));

  // (Mac 1.3.117) Macmillan's two_col_all (each prompt beside its answer): one column, nothing past the edge, the answer a
  // field's width; its tools menu (×10ʸ) a popover in the app's colours, dark in dark
  await page.setViewportSize({ width: 820, height: 720 });
  await sac().evaluate(() => { window.__simplMacmillan('go', 4); window.__simplMacmillan('theme', true); });
  const all = await until(async () => { const s = await last(); return s?.index === 3 && s.dark && s; });
  await sac().focus('.numeric-entry-input');
  const col = await until(async () => sac().evaluate(() => {
    const cards = [...document.querySelectorAll('article.module-container')].map((c) => c.getBoundingClientRect());
    const field = document.querySelector('.numeric-entry-border').getBoundingClientRect(), menu = document.querySelector('.numeric-entry-toolmenu');
    return menu.style.display !== 'none' && { lefts: [...new Set(cards.map((r) => Math.round(r.left)))], right: Math.max(...cards.map((r) => r.right)), width: innerWidth, field: Math.round(field.width), menuBg: getComputedStyle(menu).backgroundColor, menuInk: getComputedStyle(menu.querySelector('button')).color };
  }));
  check(!!all && col && col.lefts.length === 1 && col.right <= col.width && col.field <= 260, `two_col_all as one column, inside the page, the field a field's width: ${JSON.stringify(col)}`);
  check(col && col.menuBg === 'rgb(44, 44, 46)' && col.menuInk === 'rgb(242, 242, 247)', `the tools menu dark in dark, its words light: ${JSON.stringify(col && { bg: col.menuBg, ink: col.menuInk })}`);
  await sac().evaluate(() => window.__simplMacmillan('theme', false));
  await page.setViewportSize({ width: 1000, height: 720 });

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

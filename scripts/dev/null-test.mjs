#!/usr/bin/env node
// Walks every screen with the fields Canvas commonly leaves empty nulled out of every API answer,
// and fails if any of them reaches the screen as the word "null" (or NaN, or undefined). Canvas
// returns null for an ungraded assignment's points, a course with no term, a quiz with no limit, a
// submission with no score and plenty else; the mock fills all of them in, so only a run like this
// catches a line that interpolates one straight into its text.
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync } from 'node:fs';
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
const PORT = 8790;
const BASE = `http://localhost:${PORT}`;
const NULLED = new Set([
  'points_possible', 'time_limit', 'term', 'course_code', 'score', 'kept_score', 'grade',
  'current_grade', 'current_score', 'final_grade', 'final_score', 'description', 'public_description',
  'question_count', 'allowed_attempts', 'nickname', 'subtitle', 'pronouns', 'short_name', 'position',
  'unread_count', 'due_at', 'sections', 'section', 'group_weight', 'attempt', 'workflow_state',
  'message', 'preview_url', 'display_name', 'size',
]);
const scrub = (v) => {
  if (Array.isArray(v)) return v.map(scrub);
  if (v && typeof v === 'object') {
    const out = {};
    for (const [k, val] of Object.entries(v)) out[k] = NULLED.has(k) ? null : scrub(val);
    return out;
  }
  return v;
};

const failures = [];
const check = (cond, label) => {
  console.log(`${cond ? '  ✓' : '  ✗'} ${label}`);
  if (!cond) failures.push(label);
};

const extDir = join(tmpdir(), `bcv-ext-null-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) cs.matches.push(`${BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));

const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
await new Promise((r) => setTimeout(r, 700));
const context = await chromium.launchPersistentContext(join(tmpdir(), `bcv-profile-null-${Date.now()}`), {
  channel: 'chromium', headless: true, viewport: { width: 1400, height: 900 },
  args: [`--disable-extensions-except=${extDir}`, `--load-extension=${extDir}`],
});
try {
  let [sw] = context.serviceWorkers();
  if (!sw) sw = await context.waitForEvent('serviceworker', { timeout: 15000 });
  await new Promise((r) => setTimeout(r, 1200));
  for (const t of context.pages()) if (t.url().endsWith('/setup/setup.html')) await t.close();
  await sw.evaluate(() => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'setup:flow': 2 }));

  const page = await context.newPage();
  await page.route('**/api/v1/**', async (route) => {
    const res = await route.fetch().catch(() => null);
    if (!res) return route.abort();
    let body = await res.text();
    const prefix = body.startsWith('while(1);') ? 'while(1);' : '';
    try { body = prefix + JSON.stringify(scrub(JSON.parse(body.slice(prefix.length)))); } catch { /* not json */ }
    await route.fulfill({ response: res, body });
  });

  // what is on screen, including the sheets, the viewer and the tab title —
  // they hang off the document, not off the app's own element
  const sweep = () => page.evaluate(() => {
    const out = [];
    const bad = /\b(null|NaN|undefined)\b/;
    if (bad.test(document.title)) out.push({ text: `the tab title: ${document.title}`, cls: 'title' });
    const walk = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    let n;
    while ((n = walk.nextNode())) {
      const t = n.nodeValue;
      if (!t || !bad.test(t)) continue;
      const el = n.parentElement;
      if (!el || !el.offsetParent) continue; // only what a reader can see
      out.push({ text: t.replace(/\s+/g, ' ').trim().slice(0, 110), cls: el.className.slice(0, 60) });
    }
    return out;
  });

  const paths = [
    '/', '/#todo', '/#notifications', '/courses', '/groups', '/calendar', '/conversations', '/#gpa',
    '/courses/101', '/courses/101/announcements', '/courses/101/assignments', '/courses/101/discussion_topics',
    '/courses/101/grades', '/courses/101/users', '/courses/101/pages', '/courses/101/files',
    '/courses/101/quizzes', '/courses/101/modules', '/courses/101/assignments/syllabus',
    '/courses/102', '/courses/102/grades', '/courses/104/grades',
    '/courses/101/assignments/1001', '/courses/104/assignments/4002',
    '/courses/101/quizzes/9011', '/courses/101/quizzes/9001', '/courses/101/pages/course-information',
  ];
  const found = new Map();
  console.log('\nevery screen, with Canvas\'s nullable fields empty');
  for (const p of paths) {
    await page.goto(BASE + p).catch(() => {});
    await page.waitForFunction(() => {
      const c = document.documentElement.classList;
      return !c.contains('bcv-on') || c.contains('bcv-settled');
    }, null, { timeout: 15000 }).catch(() => {});
    await new Promise((r) => setTimeout(r, 400));
    const hits = [...await sweep()];
    // and whatever this screen opens over itself
    for (const sel of ['.bcv-stat', '.bcv-row--link', '.bcv-gpa__details', '.bcv-ccard']) {
      const el = await page.$(sel);
      if (!el) continue;
      await el.click({ timeout: 1500 }).catch(() => {});
      await new Promise((r) => setTimeout(r, 400));
      hits.push(...await sweep());
      await page.keyboard.press('Escape').catch(() => {});
      await new Promise((r) => setTimeout(r, 200));
    }
    for (const hit of hits) {
      const key = `${hit.cls}|${hit.text}`;
      if (!found.has(key)) found.set(key, { ...hit, where: p });
    }
  }
  for (const hit of found.values()) check(false, `${hit.where} shows "${hit.text}" (.${hit.cls.split(' ')[0]})`);
  check(found.size === 0, `no screen prints an empty Canvas field as text (${paths.length} screens walked)`);
} catch (e) {
  check(false, `crash: ${e.message}`);
} finally {
  await context.close();
  server.kill();
}
if (failures.length) {
  console.log(`\n${failures.length} check(s) failed:`);
  for (const f of failures) console.log(` - ${f}`);
  process.exit(1);
}
console.log('\nAll checks passed.');

#!/usr/bin/env node
// The request layer on its own (extension/lib/canvas-api.js, run in a sandbox with a fake fetch):
// the gate — at most ten GETs in flight, the newest navigation's requests served first when a
// slot frees, writes never held, a warm-up in flight giving its slot up to the screen being drawn
// and asked again later, a queued warm-up the new screen wants moved up to its place — a
// throttled 403 asked again rather than thrown, a refusal still thrown, and a request that never
// answers given up, asked once more, then failed.
import { existsSync, readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import vm from 'node:vm';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const src = readFileSync(join(root, 'extension', 'lib', 'canvas-api.js'), 'utf8');
const pending = []; // { url, resolve }: every fetch, answered by the test
let opened = 0, peak = 0;
const finished = [];
const sandbox = {
  self: {}, location: { host: 'canvas.test', origin: 'https://canvas.test' },
  document: { cookie: '', querySelector: () => null },
  setTimeout, clearTimeout, URL, Promise, Map, Set, Date, JSON, Array, Object, Error, String, Number, Math, Symbol, console, AbortController,
  fetch: (url, init) => new Promise((resolve, reject) => {
    opened++; peak = Math.max(peak, opened);
    const entry = { url, resolve: (status = 200, body = '[]') => { opened--; finished.push(url); resolve({ ok: status < 400, status, text: async () => body, headers: { get: () => null } }); } };
    if (init && init.signal) init.signal.addEventListener('abort', () => { opened--; const i = pending.indexOf(entry); if (i >= 0) pending.splice(i, 1); const e = new Error('aborted'); e.name = 'AbortError'; reject(e); });
    pending.push(entry);
  }),
};
vm.createContext(sandbox);
vm.runInContext(src, sandbox);
const C = sandbox.self.BCV.canvas;
const tick = (ms = 5) => new Promise((r) => setTimeout(r, ms));
let fails = 0;
const check = (ok, msg) => { console.log(`  ${ok ? '✓' : '✗'} ${msg}`); if (!ok) fails++; };
const path = (p) => p.url.replace('https://canvas.test', '');

console.log('the gate');
const first = Array.from({ length: 20 }, (_, i) => C.get(`/api/v1/a${i}`));
await tick();
check(opened === 10 && pending.length === 10, `ten in flight of twenty asked (${opened} open, ${pending.length} out)`);
C.navigated();
const pressed = ['/api/v1/p0', '/api/v1/p1', '/api/v1/p2'].map((u) => C.get(u));
await tick();
pending.shift().resolve(); await tick();
pending.shift().resolve(); await tick();
pending.shift().resolve(); await tick();
const went = pending.slice(-3).map(path);
check(went.join(',') === '/api/v1/p0,/api/v1/p1,/api/v1/p2', `the slots freed went to the newest navigation's requests, in order: ${went.join(',')}`);
const w = C.post('/api/v1/w', { a: 1 });
await tick();
check(opened === 11 && path(pending.at(-1)) === '/api/v1/w', `a write is never held (${opened} open)`);
pending.pop().resolve(); await w;
while (pending.length) { pending.shift().resolve(); await tick(); }
await Promise.all([...first, ...pressed]);
check(finished.length === 24 && opened === 0 && peak === 11, `everything answered (${finished.length} done, peak ${peak} with the write)`);

console.log('throttling and refusal');
const t0 = Date.now();
const th = C.get('/api/v1/throttled');
await tick();
pending.shift().resolve(403, '403 Forbidden (Rate Limit Exceeded)');
await tick(750);
check(pending.length === 1 && path(pending[0]) === '/api/v1/throttled', 'a throttled 403 is asked again after a moment');
pending.shift().resolve(200, '[{"id":"1"}]');
const v = await th;
check(Array.isArray(v) && v[0].id === '1' && Date.now() - t0 >= 700, `and answered on the retry after ${Date.now() - t0}ms`);
const real = C.get('/api/v1/refused');
await tick();
pending.shift().resolve(403, '{"status":"unauthorized","errors":[{"message":"user not authorized to perform that action"}]}');
let err = null;
try { await real; } catch (e) { err = e; }
check(err && err.status === 403 && /not authorized/.test(err.message) && opened === 0, `a refusal is still thrown: ${err?.message}`);

console.log('a request that never answers');
C.tune({ requestTimeout: 120 });
const hung = C.get('/api/v1/hung');
await tick();
check(pending.length === 1 && opened === 1, 'it is out');
await tick(160);
check(opened === 1 && pending.length === 1 && finished.filter((u) => /hung/.test(u)).length === 0, 'after the time allowed it was given up and asked once more (one out again)');
let hungErr = null;
try { await hung; } catch (e) { hungErr = e; }
check(hungErr && /did not answer in time/.test(hungErr.message) && opened === 0 && pending.length === 0, `the second try was given up too and the request failed, its slot freed: ${hungErr?.message}`);
const after = C.get('/api/v1/after');
await tick();
check(opened === 1, 'and the gate still lets the next request through');
pending.shift().resolve();
await after;

console.log('warm-ups make way');
C.tune({ requestTimeout: 20000 });
// a screen has settled and its warm-ups fill every slot; a press: the new screen's request goes
// out at once, in the slot the last warm-up out gives up, and that warm-up is asked again later,
// behind the new screen's own requests
C.settled();
const warm = Array.from({ length: 12 }, (_, i) => C.get(`/api/v1/warm${i}`));
await tick();
check(opened === 10 && pending.length === 10, `ten warm-ups in flight of twelve (${opened} open)`);
C.navigated();
const press = C.get('/api/v1/press');
await tick();
const out = pending.map(path);
check(opened === 10 && out.includes('/api/v1/press') && !out.includes('/api/v1/warm9') && out.includes('/api/v1/warm8'), `the press went out at once, in the slot the newest warm-up gave up: ${out.join(',')}`);
pending.splice(pending.findIndex((p) => path(p) === '/api/v1/press'), 1)[0].resolve();
await press;
await tick();
check(opened === 10 && path(pending.at(-1)) === '/api/v1/warm10', `the slot it freed went to the warm-up next in line, not the one that gave way (${path(pending.at(-1))})`);
while (pending.length) { pending.shift().resolve(); await tick(); }
await Promise.all(warm);
check(finished.filter((u) => /warm9$/.test(u)).length === 1 && opened === 0, 'the warm-up that gave way was asked again and answered');
// a warm-up asked for on the last screen, still queued when the next screen wants it: it moves up to the new screen's place
C.settled();
const queued = Array.from({ length: 12 }, (_, i) => C.cached(`memo${i}`, 0, () => C.get(`/api/v1/memo${i}`)));
await tick();
check(opened === 10 && pending.length === 10, 'ten warm-ups out, two queued');
C.navigated();
const wanted = C.cached('memo11', 0, () => C.get('/api/v1/memo11'));
pending.shift().resolve();
await tick();
check(path(pending.at(-1)) === '/api/v1/memo11', `the queued one the new screen wants went out first, ahead of the one queued before it (${path(pending.at(-1))})`);
while (pending.length) { pending.shift().resolve(); await tick(); }
await Promise.all([...queued, wanted]);
check(opened === 0 && finished.filter((u) => /memo11$/.test(u)).length === 1, 'and it was asked for once, shared by both');

// ---- the memo after a write ---------------------------------------------------------------------
// A key forgotten while its loader is still answering: the next caller asks afresh rather than
// joining the run from before the write (a star pressed while the course list loads showed unset).
{
  console.log('the memo after a write');
  const first = C.cached('after-write', 60000, () => C.get('/api/v1/after-write?n=1'));
  await tick();
  await C.invalidate('after-write');
  const second = C.cached('after-write', 60000, () => C.get('/api/v1/after-write?n=2'));
  await tick();
  const asked = pending.filter((p) => /after-write/.test(p.url)).map(path);
  check(asked.length === 2 && asked[1].endsWith('n=2'), `a caller after the invalidation starts its own request (${asked.join(', ')})`);
  for (const p of pending.filter((x) => /after-write/.test(x.url))) { pending.splice(pending.indexOf(p), 1); p.resolve(200, '[]'); }
  await first; await second;
}

// ---- the record CSV reader (extension/lib/record-csv.js) ------------------------------------------
{
  console.log('the record CSV reader');
  const csvSrc = readFileSync(join(root, 'extension', 'lib', 'record-csv.js'), 'utf8');
  const csvBox = { self: {} };
  csvBox.self = csvBox;
  vm.createContext(csvBox);
  vm.runInContext(csvSrc, csvBox);
  const R = csvBox.BCV.recordCsv;
  const tsv = R.parse('course\tgrade\tcredits\nCalculus I, Honors\tA-\t4\nWriting\tB+\t3');
  check(tsv.rows.length === 2 && tsv.rows[0].course === 'Calculus I, Honors' && tsv.rows[0].points === 3.7 && tsv.rows[0].credits === 4, `a tab-separated file keeps a comma inside a course name: ${tsv.rows.map((r) => `${r.course}=${r.grade}`).join(' | ')}`);
  const guessed = R.parse('Course,Credits,Percentage\nPhysics,4,92%\nChemistry,3,B+');
  check(guessed.rows.length === 2 && guessed.rows[0].points === 3.7 && guessed.rows[1].points === 3.3, `without a grade column named, the grade is never read off the credits: ${guessed.rows.map((r) => `${r.course}=${r.grade}→${r.points}`).join(' | ')}`);
  const hist = R.history('Fall 2025,3.5\nMay 15 2026,3.6\n2026-06-01,\nhello 2026,3.0\n1,2');
  check(hist.length === 2 && hist[0].date === '2025-12-15' && hist[1].date === '2026-05-15', `a history reads terms and written months, and skips a blank GPA, a word before a year and a bare number: ${hist.map((r) => `${r.date}=${r.gpa}`).join(' | ')}`);
}

console.log('a session that has ended');
let lostCalls = 0;
C.onSessionLost(() => { lostCalls++; });
check(C.sessionOk() === true, 'the session is fine until Canvas says otherwise');
const gone = C.get('/api/v1/users/self');
await tick();
pending.shift().resolve(401, '{"status":"unauthenticated","errors":[{"message":"user authorization required"}]}');
let goneErr = null;
try { await gone; } catch (e) { goneErr = e; }
check(goneErr && goneErr.status === 401 && /Signed out/.test(goneErr.message) && C.sessionOk() === false && lostCalls === 1, `a 401 "unauthenticated" answer ends the session and is reported once: ${goneErr?.message}`);
const fetchesBefore = finished.length + pending.length;
let nextErr = null;
try { await C.get('/api/v1/next'); } catch (e) { nextErr = e; }
check(nextErr && nextErr.status === 401 && finished.length + pending.length === fetchesBefore && lostCalls === 1, 'every request after it fails at once, without asking Canvas, and it is not reported again');
check((await C.checkSession()) === false, 'checkSession says so');

// ---- the tool-tab settings code (extension/lib/devcode.js) -----------------------------------
// The Developer section's settings travel as a short code that gets read out and typed back in, so
// what matters is that a code means the same thing at both ends.
const devSrc = readFileSync(join(root, 'extension', 'lib', 'devcode.js'), 'utf8');
const devBox = { self: {} };
devBox.self = devBox;
vm.createContext(devBox);
vm.runInContext(devSrc, devBox);
const D = devBox.BCV.devcode;
check(D.encode(D.SHIPPED) === 'SC3-200' && D.encode(D.EVERYTHING) === 'SC3-211', `the two codes worth knowing: ${D.encode(D.SHIPPED)} ships, ${D.encode(D.EVERYTHING)} says everything`);
const back = D.decode('SC3-511');
check(back.ok && back.settings.settle === 5 && back.settings.log === 1 && back.settings.toast === 1, `a code reads back as the settings it stands for: ${JSON.stringify(back.settings)}`);
check(D.encode(D.decode(D.encode(D.EVERYTHING)).settings) === D.encode(D.EVERYTHING), 'a code typed back in means what it meant');
const loose = D.decode('sc3 2 1 1');
check(loose.ok && D.encode(loose.settings) === 'SC3-211', 'spaces, lower case and a missing dash are all read the same');
const bad = D.decode('SC3-99999');
check(!bad.ok && D.encode(bad.settings) === D.encode(D.SHIPPED) && /3 digits/.test(bad.message), `a code that is not one says so and changes nothing: ${bad.message}`);
const over = D.decode('SC3-999');
check(D.encode(over.settings) === 'SC3-511', `digits past the end of a setting come back to its last value, never past it: ${D.encode(over.settings)}`);
check(!D.FIELDS.some((f) => ['mode', 'windows', 'wait', 'blank', 'close'].includes(f.key)), `nothing is caught any more, so nothing is left to tune about catching: ${D.FIELDS.map((f) => f.key).join(', ')}`);

// ---- the on-demand modules' one list (content/app/lazy-modules.js) ------------------------------
// The page (lazy.js) and the background read the same table: a module's files, in order, and what it
// needs first. The source manifest names the same files in the group whose match never fires (Safari
// parses none until asked; the iPhone app injects the group whole), and the Chrome builds leave that
// group out (Chrome would list its host as a site the extension reads) — so the two have to be one list.
console.log('the on-demand modules');
const lazyBox = { self: {} };
lazyBox.self = lazyBox;
vm.createContext(lazyBox);
vm.runInContext(readFileSync(join(root, 'extension', 'content', 'app', 'lazy-modules.js'), 'utf8'), lazyBox);
const lazyTable = lazyBox.BCV_LAZY_MODULES;
const lazyFlat = Object.values(lazyTable).flatMap((m) => m.files);
const srcManifest = JSON.parse(readFileSync(join(root, 'extension', 'manifest.json'), 'utf8'));
const lazyGroup = srcManifest.content_scripts.find((cs) => (cs.matches || []).includes('https://lazy.simplcourses.invalid/*'));
check(!!lazyGroup && JSON.stringify(lazyGroup.js) === JSON.stringify(lazyFlat), `the manifest's never-matching group names the table's files, in the table's order (${lazyFlat.length} files)`);
check(new Set(lazyFlat).size === lazyFlat.length && lazyFlat.every((f) => existsSync(join(root, 'extension', f))), 'every file once, and every one in the build');
check(Object.values(lazyTable).every((m) => (m.needs || []).every((n) => lazyTable[n] && lazyFlat.indexOf(lazyTable[n].files[0]) < lazyFlat.indexOf(m.files[0]))), 'what a module needs first is a module, listed before it (the iPhone app injects the group in that order)');
const bgScripts = srcManifest.background.scripts;
check(bgScripts.indexOf('content/app/lazy-modules.js') >= 0 && bgScripts.indexOf('content/app/lazy-modules.js') < bgScripts.indexOf('background.js') && srcManifest.content_scripts.some((cs) => (cs.js || []).indexOf('content/app/lazy-modules.js') >= 0 && cs.js.indexOf('content/app/lazy-modules.js') < cs.js.indexOf('content/app/lazy.js')), 'the table loads before the background, and before lazy.js on a page');
// lazy.js itself, with the table and nothing else: every module has a way to tell it is here
const lzBox = { self: {} };
lzBox.self = lzBox;
lzBox.BCV = { api: {} };
lzBox.BCV_LAZY_MODULES = lazyTable;
vm.createContext(lzBox);
vm.runInContext(readFileSync(join(root, 'extension', 'content', 'app', 'lazy.js'), 'utf8'), lzBox);
const lz = lzBox.BCV.lazy;
check(JSON.stringify(Object.keys(lz.MODULES)) === JSON.stringify(Object.keys(lazyTable)) && Object.values(lz.MODULES).every((m) => typeof m.has === 'function' && Array.isArray(m.files) && m.files.length > 0), `lazy.js knows every module in the table and how to tell each is here: ${Object.keys(lz.MODULES).join(', ')}`);
check(lz.toolModule('ptable') === 'tool:ptable' && lz.toolModule('calc') === null && !lz.has('setup') && !lz.has('setupcss') && lzBox.BCV.setup?.__stub === true, 'a tool’s module by its key, a built-in tool none; nothing loaded yet, the setup a stub');

// ---- the springs (content/app/motion.js, docs/MOTION.md) -----------------------------------------
// The motion is a damped spring solved exactly; what the interface relies on is that every preset
// settles in the time its rule is given, overshoots only where that is wanted, hands its position
// and speed on when cut short, and reads the same as a CSS linear() easing. The engine is loaded
// with no document at all: it must stand up on its own, and install nothing where it cannot.
console.log('the springs');
const motionSrc = readFileSync(join(root, 'extension', 'content', 'app', 'motion.js'), 'utf8');
const mbox = { self: {}, console, Math, Map, Number, Object, String, JSON, Promise, Array };
vm.createContext(mbox);
vm.runInContext(motionSrc, mbox);
const M = mbox.self.BCV.motion;
check(!!M && typeof M.spring === 'function' && M.supportsLinear === false && M.installTokens() === false, 'the engine stands up with no window and no CSS, and installs no tokens where linear() is unknown');
const peakOf = (sp) => { let peak = -Infinity; for (let t = 0; t <= sp.settle; t += 0.002) peak = Math.max(peak, sp.at(t)); return peak; };
const times = Object.fromEntries(Object.keys(M.PRESETS).map((k) => [k, Math.round(M.spring(k).settle * 1000)]));
check(Object.values(times).every((ms) => ms >= 240 && ms <= 520) && times.snappy < times.gentle && times.snappy >= 280 && times.gentle >= 440 && times.island >= 480 && times.island <= 520 && times.island > times.gentle, `every preset settles within half a second, none in a blink — snappy under gentle, island (the tray's capsules) the half second itself: ${JSON.stringify(times)}`);
check(peakOf(M.spring('snappy')) > 1.02 && peakOf(M.spring('snappy')) < 1.08, `snappy overshoots a little (pins, ticks): ${((peakOf(M.spring('snappy')) - 1) * 100).toFixed(1)}%`);
check(['gentle', 'settle', 'scrim', 'phone', 'island'].every((k) => Math.abs(peakOf(M.spring(k)) - 1) < 0.006), 'gentle, settle, scrim, phone and island land without a visible overshoot (nothing that carries text bounces)');
check(['gentle', 'snappy'].every((k) => { const sp = M.spring(k); return Math.abs(sp.at(sp.settle) - 1) < 0.011 && Math.abs(sp.at(0)) < 1e-9; }), 'each starts at rest and is at its end when it settles');
const cutSp = M.spring('gentle'); const tc = 0.06; const pc = cutSp.at(tc), vc = cutSp.velocity(tc);
const turned = M.spring('gentle', { from: pc, to: 0, v0: vc });
check(vc > 0 && Math.abs(turned.at(0) - pc) < 1e-9 && turned.at(0.02) > pc && turned.at(0.16) < pc && turned.at(turned.settle) < 0.011, `a motion cut short carries on from where it is at the speed it had, and only then turns back: at ${pc.toFixed(2)} moving ${vc.toFixed(1)}/s, still ${turned.at(0.02).toFixed(2)} after 20 ms, ${turned.at(0.16).toFixed(2)} after 160 ms`);
const ease = M.easing('gentle');
check(/^linear\(0, [\d.]+ [\d.]+%, .* 1\)$/.test(ease) && ease.split(',').length >= 60 && ease.split(',').length <= 193 && (times.gentle / (ease.split(',').length - 1)) <= 6 && Math.round(M.duration('gentle') * 1000) === times.gentle, `a preset reads as a CSS linear() easing over its own settle time, a stop every ~5 ms (finer than a 120 Hz frame): ${ease.split(',').length} stops, ${times.gentle}ms`);
const heavy = M.spring({ stiffness: 400, damping: 50, mass: 1 });
check(heavy.zeta > 1 && peakOf(heavy) <= 1 + 1e-9 && heavy.at(heavy.settle) > 0.985 && heavy.settle < 0.7, `an over-damped spring creeps in without ever passing its end (ζ ${heavy.zeta.toFixed(2)}, ${Math.round(heavy.settle * 1000)}ms)`);
const noEl = M.run(null, { opacity: [1, 0] }, 'scrim');
check(noEl.done === true && typeof noEl.cancel === 'function', 'run() on nothing is a finished no-op, never a throw');

// ---- Safari (scripts/dev/safari-lint.mjs, docs/SAFARI.md) -------------------------------------
// The suites run in Chromium and the product is a Safari extension: every script API and CSS feature
// Safari lacks, or got after the floor the Mac app supports, is checked against a table — a script
// line without a guard, a backdrop-filter without its prefix, a rule that needs what Safari has not
// got, all fail here.
console.log('Safari');
const lint = spawnSync(process.execPath, [join(root, 'scripts', 'dev', 'safari-lint.mjs'), join(root, 'extension'), '--json'], { encoding: 'utf8' });
let lintOut = null;
try { lintOut = JSON.parse(lint.stdout); } catch { lintOut = null; }
check(!!lintOut && lintOut.files > 40, `the lint read the extension (${lintOut ? `${lintOut.files} files, floor Safari ${lintOut.floor}` : `no report: ${(lint.stderr || lint.stdout || '').slice(0, 200)}`})`);
check(!!lintOut && lintOut.fails.length === 0 && lint.status === 0, lintOut && lintOut.fails.length ? `${lintOut.fails.length} line(s) Safari cannot run: ${lintOut.fails.map((f) => `${f.file}:${f.line} ${f.rule}`).join(' | ')}` : 'nothing Safari cannot do: no unguarded API below the floor, every backdrop-filter prefixed, no rule that needs what Safari lacks');
check(!!lintOut && lintOut.notes.some((n) => /requestIdleCallback/.test(n.rule) && /guarded/.test(n.fix)), 'and the APIs Safari lacks are on guarded lines (requestIdleCallback falls back to setTimeout)');

// ---- plain words (content/app/hub.js: understand, resolve, readingLabel, toolNameOf) ------------
// The hub stood up with no browser — a stub store holding two courses' work — and phrases read into
// a verb, a kind, courses, a span and title words, resolved against the rows and scored.
console.log('plain words');
const at = (d, hh = 23, mm = 59) => { const x = new Date(); x.setDate(x.getDate() + d); x.setHours(hh, mm, 0, 0); return x.toISOString(); };
const hubCalls = [];
const hubCourses = [{ id: 101, name: 'F26-MATH 021 20', code: 'MATH-021-20', state: 'current' }, { id: 102, name: 'F26-PHYS 008 01', code: 'PHYS-008-01', state: 'current' }];
const hubData = {
  assignments: {
    101: [
      { id: 1010, name: 'Qz01', due_at: at(0), submission_types: ['online_quiz'], is_quiz_assignment: true, quiz_id: 9010, submission: {} },
      { id: 1011, name: 'Lec06-PreQuiz', due_at: at(1, 10, 30), submission_types: ['online_quiz'], is_quiz_assignment: true, quiz_id: 9011, submission: {} },
      { id: 1012, name: 'Composition of Functions', due_at: at(1), submission_types: ['online_upload'], submission: {} },
      { id: 1009, name: 'Dis01', due_at: at(0), submission_types: ['online_text_entry'], submission: {} },
    ],
    102: [
      { id: 2003, name: 'Lab 2', due_at: at(2), submission_types: ['online_upload'], submission: {} },
      { id: 2001, name: 'Lab 1 report', due_at: at(-7), submission_types: ['online_upload'], submission: { submitted_at: at(-8) } },
      { id: 2004, name: 'W3 HW', due_at: at(6), submission_types: ['online_upload'], submission: {}, description: '<p>Work the set on <a href="https://webassign.example.com/w3">WebAssign</a> and the <a href="/courses/102/files/1">sheet</a>.</p>' },
      { id: 2050, name: 'Knewton Alta: Unit 2', due_at: at(5), submission_types: ['external_tool'], external_tool_tag_attributes: { url: 'https://tool.example.com/launch' }, submission: {} },
    ],
  },
  quizzes: { 101: [{ id: 9010, title: 'Qz01', due_at: at(0), assignment_id: 1010 }, { id: 9011, title: 'Lec06-PreQuiz', due_at: at(1, 10, 30), assignment_id: 1011 }], 102: [{ id: 9019, title: 'Lec08-PreQuiz', due_at: at(8), assignment_id: 2019 }] },
  discussions: { 101: [], 102: [{ id: 7001, title: 'Intro thread', posted_at: at(-3), last_reply_at: at(-1) }] },
  modules: { 101: [], 102: [{ id: 'm2', name: 'Week 2: Forces', items: [{ id: 'i8', type: 'ExternalTool', title: 'Mastering Physics', external_url: 'https://tool.example.com/mastering' }, { id: 'i6', type: 'ExternalUrl', title: 'PhET simulation', external_url: 'http://sim.test/sim' }] }] },
};
const hubU = {
  dayDiff: (d, now = new Date()) => Math.round((new Date(d.getFullYear(), d.getMonth(), d.getDate()) - new Date(now.getFullYear(), now.getMonth(), now.getDate())) / 864e5),
  fmtShort: (d) => d.toDateString().slice(4, 10), fmtLong: (d) => d.toDateString(), fmtTime: () => '11:59 PM', fmtRecent: () => 'yesterday', whenShort: () => 'soon', parse: (s) => (s ? new Date(s) : null), plural: (n, w, p) => `${n} ${n === 1 ? w : p || `${w}s`}`,
};
const hubStore = {
  assignments: async (id) => hubData.assignments[id] || [], quizzes: async (id) => hubData.quizzes[id] || [], discussions: async (id) => hubData.discussions[id] || [], modules: async (id) => hubData.modules[id] || [], tabs: async () => [], announcementsFeed: async () => [], courses: async () => hubCourses, people: async () => [],
  workStatus: (a, s) => ({ word: (s || a.submission || {}).submitted_at ? 'Submitted' : 'Not submitted' }), fmtPts: (n) => String(n),
};
const hubSelf = { BCV: { utils: { h: () => null, overlayRoot: () => null }, ui: hubU, IC: new Proxy({}, { get: (_, k) => k }), store: hubStore, app: { go: (h) => hubCalls.push(h) }, exttool: { open: (o) => hubCalls.push(`tool:${o.url}`) } } };
new Function('self', 'location', readFileSync(join(root, 'extension', 'content', 'app', 'hub.js'), 'utf8'))(hubSelf, { origin: 'https://canvas.test', host: 'canvas.test' });
const hub = hubSelf.BCV.hub;
const read = (raw, opts) => hub.understand(raw, hubCourses, opts);
const rowsFor = async (raw, opts) => { const r = read(raw, opts); const rows = await hub.resolve(r, { cs: hubCourses }); return { r, rows, titles: rows.map((x) => x.title), label: rows.label }; };
let p = read("today's math quiz", { verb: 'start' });
check(p.verb === 'start' && p.kind === 'quiz' && p.courses.join() === '101' && p.span?.lo === 0 && p.span?.hi === 0 && p.words.length === 0 && hub.readingLabel(p, hubCourses) === 'Start a quiz · in MATH-021-20 · today', `"today's math quiz" under /start: the kind, the course by a word of its name, the day, no title words, read back as "${hub.readingLabel(p, hubCourses)}"`);
p = read('physics lab due this week');
const dow = new Date().getDay();
check(p.courses.join() === '102' && p.words.join() === 'lab' && p.courseWords.join() === 'physics' && p.span?.lo === 0 && p.span?.hi === 6 - dow && p.span.label === 'this week' && p.strong, `"physics lab due this week": the course, "lab" left as the title word, the week to its end (${JSON.stringify(p.span)}), strong enough for a plain search`);
p = read("what's due tomorrow");
check(p.verb === 'find' && p.span?.lo === 1 && p.span?.hi === 1 && p.strong && !p.kind, `"what's due tomorrow": the possessive dropped, "what" the verb, tomorrow the span`);
check(!read('dis01').strong && !read('dis01').natural && !read('lab 2').strong && read('physics').natural && !read('physics').strong && read('open').natural && !read('open').strong && read('tomorrow').strong, 'a name alone, a course alone, a verb alone read as nothing more than words (a plain search stays as it was); a day alone is a phrase');
p = read('i want to open the lab tool');
check(p.verb === 'open' && p.kind === 'tool' && p.words.join() === 'lab' && p.kindWords.join() === 'tool', `"i want to open the lab tool": fillers dropped, the verb, the kind, the word: ${JSON.stringify({ verb: p.verb, kind: p.kind, words: p.words })}`);
p = read('what do i have due friday');
const fri = (5 - dow + 7) % 7;
check(p.verb === 'find' && p.span?.lo === fri && p.span?.hi === fri && p.span.label === 'Friday' && read('in 3 days').span?.lo === 3 && read('next week').span?.lo === 7 - dow && read('overdue physics work').span?.hi === -1 && read('oct 3').span?.label.startsWith('Oct'), `days in words: friday (${fri} days off), in 3 days, next week, overdue, a date`);
p = read('the lab tool', { kind: 'tool' });
check(p.natural && p.kind === 'tool' && p.words.join() === 'lab' && !read('knew', { kind: 'tool' }).natural && read('knewton unit 2', { kind: 'tool' }).words.join() === 'knewton,unit,2', 'under /open: "the lab tool" is a phrase (fillers, the kind), "knew" is a name for the finder as before, "unit" stays a title word when the kind is forced');
let got = await rowsFor("what's due tomorrow");
check(got.titles.join(' · ') === 'Lec06-PreQuiz · Composition of Functions' && got.label === 'Find work · tomorrow', `resolved: everything due tomorrow, the soonest first, the quiz listed once though it is an assignment too: ${got.titles.join(' · ')}`);
got = await rowsFor("today's math quiz", { verb: 'start' });
got.rows[0]?.run?.();
check(got.titles.join() === 'Qz01' && /· Enter starts it$/.test(got.rows[0].sub) && hubCalls.at(-1) === '/courses/101/quizzes/9010?bcv=take', `resolved under /start: the quiz due today, alone (the course's other quiz is tomorrow), Enter opening it to take (${hubCalls.at(-1)})`);
got = await rowsFor('physics lab due this week');
check(got.titles[0] === 'Lab 2' && got.titles.every((t) => /lab/i.test(t)) && (2 <= 6 - dow ? got.titles.join() === 'Lab 2' : got.titles.length >= 2), `resolved: the lab due in two days first, only rows holding "lab" (${got.titles.join(' · ')}${2 <= 6 - dow ? ', the one in the week alone' : ', none in what is left of the week, so the nearest'})`);
got = await rowsFor('mastering physics', { kind: 'tool' });
check(got.titles.join() === 'Mastering Physics' && got.label === 'Find a tool · “mastering” · in PHYS-008-01', `under /open, "mastering physics": the module's tool, the course read from "physics": ${got.titles.join()}`);
got = await rowsFor('the lab tool', { kind: 'tool' });
check(got.titles.length === 0 && got.label === 'Find a tool · “lab”', 'under /open, "the lab tool" with no tool by that word lists nothing rather than everything');
got = await rowsFor('', { kind: 'tool' });
check(got.titles.join(' · ') === 'Knewton Alta · Knewton Alta: Unit 2 · WebAssign · Mastering Physics · PhET simulation', `every tool: the one behind an assignment by its own name, the assignment that is the tool, a link in an assignment's instructions, a module's tool and its link: ${got.titles.join(' · ')}`);
check(got.rows[0].sub === 'PHYS-008-01 · Tool · via Knewton Alta: Unit 2' && got.rows[2].sub.startsWith('PHYS-008-01 · Link in W3 HW · due '), `each saying where it came from: “${got.rows[0].sub}”, “${got.rows[2].sub}”`);
got = await rowsFor('math', { verb: 'find' });
check(got.titles.length === 4 && new Set(got.titles).size === 4 && got.titles.includes('Dis01') && got.titles.includes('Qz01'), `"/find math": the course's quizzes and assignments, each once: ${got.titles.join(' · ')}`);
got = await rowsFor('overdue physics work');
check(got.titles.join() === 'Lab 1 report' && got.label === 'Find an assignment · in PHYS-008-01 · overdue', `"overdue physics work": the one past due: ${got.titles.join()}`);
got = await rowsFor('check my physics grades');
check(got.rows[0]?.href === '/courses/102/grades' && got.label === 'Find grades · in PHYS-008-01', `"check my physics grades" goes to that course's grades: ${got.rows[0]?.href}`);
got = await rowsFor('calendar friday');
got.rows[0]?.run?.();
check(got.rows.length === 1 && /^\/calendar\?date=\d{4}-\d{2}-\d{2}$/.test(hubCalls.at(-1)), `"calendar friday" opens the calendar on that day: ${hubCalls.at(-1)}`);
check(hub.toolNameOf({ name: 'Knewton Alta: Unit 2', external_tool_tag_attributes: { url: 'https://tool.example.com/launch' } }) === 'Knewton Alta' && hub.toolNameOf({ name: 'Ch 5 HW', external_tool_tag_attributes: { url: 'https://session.masteringphysics.com/x' } }) === 'Mastering' && hub.toolNameOf({ name: 'Unit 4 - Homework', external_tool_tag_attributes: { url: 'https://app.knewton.com/lti' } }) === 'Knewton Alta' && hub.toolNameOf({ name: 'Week 3: Forces', external_tool_tag_attributes: { url: 'https://tool.example.com/x' } }) === null && hub.toolNameOf({ name: 'Lec08-PreQuiz', external_tool_tag_attributes: {} }) === null, 'a tool\'s name: the publisher behind a known launch address, else the words before the colon unless they only say which unit or week');
check(hub.parse("/what's due tomorrow").cmd?.name === 'find' && hub.parse('/take physics quiz').cmd?.name === 'start' && hub.parse('/physics quiz tomorrow').cmd === null && hub.matchCommands('physics').length === 0 && hub.byName('launch')?.name === 'open', '"/what\'s" is /what (a name of /find), /take is /start, "/physics …" names no command (search.js reads it as a phrase)');
check(hub.COMMANDS.every((c) => (c.label || c.hint).length <= 32) && hub.byName('open').hint === 'Open a tool in its own tab', `every command heads its list with a short title (the longest: ${Math.max(...hub.COMMANDS.map((c) => (c.label || c.hint).length))} characters), /open's line under it one clause`);

// ---- the calculator's line as LaTeX (content/app/tools/calc-latex.js) ----------------------------
// Pure: the line the calculator shows, read with its own grammar into a tree and written out as
// LaTeX — what the calculator's display typesets (in the tool and in the pin's panel) and copies.
console.log('calculator LaTeX');
const texSelf = {};
new Function('self', readFileSync(join(root, 'extension', 'content', 'app', 'tools', 'calc-latex.js'), 'utf8'))(texSelf);
const TX = texSelf.BCV.calcTex;
const texIs = (src, want, why) => check(TX.latex(src) === want, `${why}: ${src || '(nothing)'} → ${TX.latex(src)}`);
texIs('7×6', '7 \\times 6', 'a product');
texIs('2÷3', '\\frac{2}{3}', 'a division is a fraction');
texIs('3×4÷5', '\\frac{3 \\times 4}{5}', 'a fraction takes what came before it, as the calculator works it');
texIs('√(16)', '\\sqrt{16}', 'a square root');
texIs('∛(27)', '\\sqrt[3]{27}', 'a cube root');
texIs('8^(1÷3)', '\\sqrt[3]{8}', 'the y-th root key (x^(1÷y)) is a root, not a power');
texIs('8^(1÷', '\\sqrt[\\square]{8}', 'a root still wanting its index shows a square there');
texIs('sin(30)+cos⁻¹(0.5)', '\\sin\\left(30\\right) + \\cos^{-1}\\left(0.5\\right)', 'trig and its inverse');
texIs('sinh⁻¹(1)', '\\sinh^{-1}\\left(1\\right)', 'hyperbolic');
texIs('e^(2)', 'e^{2}', 'e to the x: the key\'s bracket is the braces');
texIs('10^(3)', '10^{3}', '10 to the x');
texIs('2^3^2', '2^{3^{2}}', 'a power from the right');
texIs('(2+3)²', '\\left(2 + 3\\right)^{2}', 'squared, over a bracket');
texIs('5!', '5!', 'a factorial');
texIs('50%', '50\\%', 'a percent');
texIs('4⁻¹', '4^{-1}', 'one over x');
texIs('2π', '2 \\pi', 'pi, side by side with a number');
texIs('2(3+4)', '2 \\left(3 + 4\\right)', 'a number against a bracket');
texIs('ln(e)', '\\ln\\left(e\\right)', 'ln and e');
texIs('log(100)', '\\log_{10}\\left(100\\right)', 'log is base ten');
texIs('log(8,2)', '\\log_{2}\\left(8\\right)', 'log with a base');
texIs('log₂(8)', '\\log_{2}\\left(8\\right)', 'log base two');
texIs('2E−3', '2 \\times 10^{-3}', 'EE is times ten to the');
texIs('1.5E12', '1.5 \\times 10^{12}', 'and a positive exponent');
texIs('−5×2', '-5 \\times 2', 'a minus in front');
texIs('2×−3', '2 \\times -3', 'a minus after an operator');
texIs('2+', '2 + \\square', 'a number still wanted is a square');
texIs('√(', '\\sqrt{\\square}', 'inside a root too');
texIs('2×(3+', '2 \\times \\left(3 + \\square\\right)', 'a bracket left open is closed for it');
texIs('.5', '0.5', 'a leading point gets its zero');
texIs('2.', '2', 'a trailing point goes');
check(TX.latex('2E') === null && TX.latex('') !== null, 'a line that cannot be read yet (an exponent with no digits) is null, an empty line is not');
check(TX.numTex(42) === '42' && TX.numTex(-0.5) === '-0.5' && TX.numTex(1e9) === '1{,}000{,}000{,}000' && TX.numTex(-1234.5) === '-1{,}234.5' && TX.numTex(1.2e15) === '1.2 \\times 10^{15}' && TX.numTex(2e-12) === '2 \\times 10^{-12}' && TX.numTex(1 / 3) === '0.333333333333' && TX.numTex(NaN) === '\\text{Error}', `a result as LaTeX: digits grouped in thousands as the display groups them, the display's own cut-offs as a × 10 to a power: ${[42, -0.5, 1e9, -1234.5, 1.2e15, 2e-12, 1 / 3].map(TX.numTex).join(' | ')}`);

// ---- settings (lib/settings.js): Away Refresh off unless turned on, and settings written before 2.98.13 brought up to that once ----
console.log('settings');
const fakeStore = (init) => { let data = init; return { storage: { local: { get: async () => ({ settings: data }), set: async (o) => { data = o.settings; } }, onChanged: { addListener() {}, removeListener() {} } }, read: () => data }; };
const loadSettings = (init) => { const s = {}; const c = fakeStore(init); new Function('self', 'chrome', readFileSync(join(root, 'extension', 'lib', 'settings.js'), 'utf8'))(s, c); return { S: s.BCV.settings, c }; };
{ const { S, c } = loadSettings(undefined); const got = await S.get(); check(got.appearance.awayRefresh === false && got.version === 3 && c.read() === undefined, 'a fresh install has Away Refresh off, and nothing is written for it'); }
{ const { S, c } = loadSettings({ version: 2, appearance: { awayRefresh: true, darkMode: 'on' }, domains: ['https://canvas.test'] }); const got = await S.get(); check(got.appearance.awayRefresh === false && got.appearance.darkMode === 'on' && got.domains.join() === 'https://canvas.test' && c.read().version === 3 && c.read().appearance.awayRefresh === false, `settings written before 2.98.13 (version 2, Away Refresh on) come up with it off, once, written back as version 3, the rest kept: ${JSON.stringify(c.read())}`); }
{ const { S, c } = loadSettings({ version: 3, appearance: { awayRefresh: true } }); check((await S.get()).appearance.awayRefresh === true && c.read().appearance.awayRefresh === true, 'turned on since (version 3), it stays on'); }
{ const { S, c } = loadSettings({ appearance: { awayRefresh: true } }); check((await S.get()).appearance.awayRefresh === false && c.read().version === 3, 'a record with no version at all counts as before: off once, and versioned'); }
// the switch's red list (2.98.18): off for a while is saved as the time it ends; on is read through lookOn, written through lookPatch
{
  const { S } = loadSettings({});
  const now = 1_000_000;
  check(S.DEFAULTS.appearance.offUntil === 0 && S.lookOn({}, now) && S.lookOn({ appearance: { skin: true } }, now) && !S.lookOn({ appearance: { skin: false, offUntil: 0 } }, now) && !S.lookOn({ appearance: { skin: false, offUntil: now + 1 } }, now) && S.lookOn({ appearance: { skin: false, offUntil: now } }, now) && S.lookOn({ appearance: { skin: true, offUntil: now + 5 } }, now), 'on unless turned off; off indefinitely stays off; off for a while is over the moment its time comes (nothing has to write that back)');
  check(JSON.stringify(S.lookPatch(true, 123)) === '{"appearance":{"skin":true,"offUntil":0}}' && JSON.stringify(S.lookPatch(false)) === '{"appearance":{"skin":false,"offUntil":0}}' && JSON.stringify(S.lookPatch(false, 5000)) === '{"appearance":{"skin":false,"offUntil":5000}}' && JSON.stringify(S.lookPatch(false, -3)) === '{"appearance":{"skin":false,"offUntil":0}}', 'the patch that saves the look: on clears any end, so an old one can never turn Simpl on later; off carries its end, or none for indefinitely');
  const saved = await S.update(S.lookPatch(false, now + 60000));
  const back = await S.update(S.lookPatch(true));
  check(saved.appearance.skin === false && saved.appearance.offUntil === now + 60000 && back.appearance.skin === true && back.appearance.offUntil === 0, 'written through the store: off with its end, then on with the end cleared');
}

console.log(fails ? `\n${fails} check(s) failed` : '\nAll checks passed.');
process.exit(fails ? 1 : 0);

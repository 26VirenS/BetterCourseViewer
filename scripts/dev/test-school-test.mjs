#!/usr/bin/env node
// The iPhone app's test school (test.simpl.com) without an iPhone: the mock Canvas as the app carries it
// (ios/SimplCourses/Resources/TestSchool/mock.js, written by make-test-school.mjs) run in a browser page, as the app's
// hidden web view runs it, and asked what the app asks — through window.__mockHandle, as Web/TestSchool.swift asks it —
// beside the mock itself run by Node, as the suites run it. Every answer must be the mock's own: the same status, the
// same type, the same body, byte for byte where it is a file — apart from what the app's copy is meant to change (the
// mock's own addresses made the test school's, the session's CSRF token on a Canvas page, no cookie) and the clock (each
// mock reads its own "now"). Also: the copy committed is the one make-test-school.mjs writes, a request's state carries
// from one call to the next (a note made is then listed), the sign-in page is the test school's, and the interface (the
// extension bundled as the app bundles it, its chrome on) comes up on the school with every request answered by the
// page, and writes to it with no cookie to read the CSRF token from — as at the app's own scheme.
// Requires Playwright (project or global install).
import { createRequire } from 'node:module';
import { spawn, spawnSync, execSync } from 'node:child_process';
import { readFileSync, mkdtempSync, rmSync } from 'node:fs';
import { createServer } from 'node:net';
import { join, dirname } from 'node:path';
import { tmpdir } from 'node:os';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { appBundle } from './app-bundle.mjs';

const require = createRequire(import.meta.url);
let chromium;
try {
  ({ chromium } = require('playwright'));
} catch {
  const prefix = execSync('npm root -g').toString().trim();
  ({ chromium } = createRequire(join(prefix, 'x.js'))('playwright'));
}

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const shipped = join(root, 'ios', 'SimplCourses', 'Resources', 'TestSchool');
const ORIGIN = 'simpltest://test.simpl.com';
const CSRF = 'mock+csrf/token=';
const failures = [];
const check = (cond, label) => {
  console.log(`${cond ? '  ✓' : '  ✗'} ${label}`);
  if (!cond) failures.push(label);
};

// ---- the copy committed is the one the generator writes -------------------------------------------------------------
const fresh = mkdtempSync(join(tmpdir(), 'simpl-test-school-'));
const made = spawnSync(process.execPath, [join(root, 'scripts', 'dev', 'make-test-school.mjs'), '--out', fresh], { encoding: 'utf8' });
check(made.status === 0, `make-test-school.mjs runs (${(made.stderr || '').trim() || 'ok'})`);
for (const f of ['mock.js', 'index.html']) {
  let same = false;
  try { same = readFileSync(join(fresh, f), 'utf8') === readFileSync(join(shipped, f), 'utf8'); } catch { /* missing */ }
  check(same, `the committed ${f} is what make-test-school.mjs writes (else: node scripts/dev/make-test-school.mjs)`);
}
rmSync(fresh, { recursive: true, force: true });

// ---- the mock as the suites run it, on a port of its own (and the one after it as its second origin) ----------------
const port = await new Promise((resolve) => { const s = createServer(); s.listen(0, () => { const p = s.address().port; s.close(() => resolve(p)); }); });
const node = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(port), String(port + 1)], { stdio: 'ignore' });
for (let i = 0; i < 50; i++) {
  try { await fetch(`http://localhost:${port}/api/v1/users/self`); break; } catch { await new Promise((r) => setTimeout(r, 100)); }
}

// ---- the app's copy, in a page as the app's hidden web view loads it ------------------------------------------------
const browser = await chromium.launch({ channel: 'chromium' });
const context = await browser.newContext({ timezoneId: Intl.DateTimeFormat().resolvedOptions().timeZone });
const page = await context.newPage();
const pageErrors = [];
page.on('pageerror', (e) => pageErrors.push(String(e)));
await page.goto(pathToFileURL(join(shipped, 'index.html')).href);
await page.waitForFunction(() => window.__mockReady === true, null, { timeout: 10000 }).catch(() => {});
check(await page.evaluate(() => typeof window.__mockHandle === 'function' && typeof window.__mockLoginPage === 'function'), 'the page offers __mockHandle and __mockLoginPage');

const b64 = (s) => Buffer.from(s || '', 'utf8').toString('base64');
/** The app's copy: what __mockHandle answers, as the app reads it. */
async function viaApp(method, path, headers = {}, body = '') {
  const r = await page.evaluate(([m, u, h, b]) => window.__mockHandle(m, u, h, b), [method, path, JSON.stringify(headers), b64(body)]);
  return { status: r.status, headers: r.headers, bytes: Buffer.from(r.bodyBase64, 'base64') };
}
/** The mock itself, over HTTP. */
async function viaNode(method, path, headers = {}, body = '') {
  const r = await fetch(`http://localhost:${port}${path}`, { method, headers, body: body || undefined, redirect: 'manual' });
  const h = {};
  r.headers.forEach((v, k) => { h[k] = v; });
  return { status: r.status, headers: h, bytes: Buffer.from(await r.arrayBuffer()) };
}

// What the app's copy is meant to change of an answer, written out again here from what make-test-school.mjs says it
// does: the mock's two addresses become the test school's, and a Canvas page carries the CSRF token in a meta tag.
const TEXTY = /^(text\/|application\/(json|javascript|xml|xhtml\+xml|x-www-form-urlencoded)|image\/svg\+xml)/i;
const LOCAL = [`http://localhost:${port}`, `http://localhost:${port + 1}`];
const swap = (t) => LOCAL.reduce((s, from) => s.split(from).join(ORIGIN).split(encodeURIComponent(from)).join(encodeURIComponent(ORIGIN)), t);
function asTheAppSeesIt(a) {
  const type = a.headers['content-type'] || '';
  const location = a.headers.location ? swap(a.headers.location) : undefined;
  if (!TEXTY.test(type)) return { ...a, text: null, location };
  // (the words of the real university the mock was first drawn from are the test school's own in the app: make-test-school.mjs)
  let text = swap(a.bytes.toString('utf8')).replaceAll('ucmerced.edu', 'test.simpl.com').replaceAll('UC Merced', 'Simpl Test School').replaceAll('Hello Bobcat!', 'Hello!').replaceAll('Bobcat', 'Student');
  if (/^text\/html/i.test(type) && text.includes('ENV = ')) text = text.replace(/<head>/i, `<head><meta name="csrf-token" content="${CSRF}">`);
  return { ...a, text, location };
}
// (the clock: each mock reads "now" when it starts, and a few answers say the time to the millisecond)
const ISO = /\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{3})?Z/g;
const unclocked = (t) => (t === null ? null : t.replace(ISO, '<time>').replace(/\b1\d{12}\b/g, '<now>'));

async function same(label, method, path, headers = {}, body = '') {
  const want = asTheAppSeesIt(await viaNode(method, path, headers, body));
  const got = await viaApp(method, path, headers, body);
  const gotText = TEXTY.test(got.headers['content-type'] || '') ? got.bytes.toString('utf8') : null;
  const ok = got.status === want.status
    && (got.headers['content-type'] || '') === (want.headers['content-type'] || '')
    && (got.headers.location || '') === (want.location || '')
    && (want.text === null ? Buffer.compare(got.bytes, want.bytes) === 0 : unclocked(gotText) === unclocked(want.text));
  check(ok, `${label}: ${method} ${path} → ${got.status} ${got.headers['content-type'] || ''}${got.headers.location ? ` → ${got.headers.location}` : ''} (${got.bytes.length} bytes), as the mock answers`);
  if (!ok) {
    console.log(`      mock: ${want.status} ${want.headers['content-type']} ${want.location || ''} ${(want.text ?? `<${want.bytes.length} bytes>`).slice(0, 300)}`);
    console.log(`      app:  ${got.status} ${got.headers['content-type']} ${got.headers.location || ''} ${(gotText ?? `<${got.bytes.length} bytes>`).slice(0, 300)}`);
  }
  return { got, gotText, json: () => JSON.parse((gotText || '').replace(/^while\(1\);/, '')) };
}

const API = { accept: 'application/json+canvas-string-ids', 'x-requested-with': 'XMLHttpRequest' };
const WRITE = { ...API, 'content-type': 'application/json', 'x-csrf-token': CSRF };

console.log('the API the app reads');
const me = await same('who is signed in', 'GET', '/api/v1/users/self', API);
check(me.json().name === 'Sam Student', 'the student is Sam Student');
await same('the profile', 'GET', '/api/v1/users/self/profile', API);
const courses = await same('the courses', 'GET', '/api/v1/courses?include[]=term&include[]=total_scores&include[]=favorites&per_page=100', API);
check(courses.json().some((c) => c.id === '101' && c.name === 'F26-MATH 021 20'), 'MATH 021 is among them');
const work = await same("a course's assignments", 'GET', '/api/v1/courses/101/assignments?include[]=submission&include[]=score_statistics&per_page=100', API);
check(work.json().length >= 10, `MATH 021 has its work (${work.json().length} assignments)`);
await same("a course's assignment groups", 'GET', '/api/v1/courses/101/assignment_groups?include[]=assignments&include[]=submission', API);
await same('the planner', 'GET', '/api/v1/planner/items?per_page=100', API);
await same('the dashboard cards', 'GET', '/api/v1/dashboard/dashboard_cards', API);
const history = await same('the history (addresses of the mock\'s own in it)', 'GET', '/api/v1/users/self/history', API);
check(history.json()[0].visited_url === `${ORIGIN}/courses/101/assignments/1002`, `an address of the mock's own is the test school's (${history.json()[0].visited_url})`);
await same('a page', 'GET', '/api/v1/courses/101/pages/course-information', API);
await same('the Inbox', 'GET', '/api/v1/conversations?scope=inbox', API);
await same('a discussion', 'GET', '/api/v1/courses/101/discussion_topics/7003/view', API);
await same('modules (a link to somebody else\'s site in them)', 'GET', '/api/v1/courses/102/modules?include[]=items', API);
const launch = await same("Knewton Alta's launch", 'GET', '/api/v1/courses/104/external_tools/sessionless_launch?launch_type=assessment&assignment_id=4003', API);
check(launch.json().url === `${ORIGIN}/mock-alta/learn/course/c1/assignment/a1`, `the tool opens at the test school's address (${launch.json().url})`);
await same("Alta's own content answer", 'GET', '/mock-alta/api/content?assignmentId=a1');
await same('something not there', 'GET', '/api/v1/courses/999999/assignments/1', API);

console.log('pages, files and a redirect');
const home = await same('the dashboard page', 'GET', '/');
check((home.gotText || '').includes(`<meta name="csrf-token" content="${CSRF}">`), 'a Canvas page carries the CSRF token where the interface looks for it');
check(!('set-cookie' in home.got.headers), 'no cookie (a custom scheme keeps none)');
await same('an assignment page', 'GET', '/courses/101/assignments/1001');
await same('a quiz\'s results page', 'GET', '/courses/101/quizzes/9001/history');
const take = await same('a quiz with no attempt open', 'GET', '/courses/101/quizzes/9014/take');
check(take.got.status === 302 && take.got.headers.location === '/courses/101/quizzes/9014', 'it is sent on to the quiz, as Canvas does (a redirect)');
const noResults = await same('a quiz with no results yet', 'GET', '/courses/101/quizzes/9010/history');
check(noResults.got.status === 302 && noResults.got.headers.location === '/courses/101/quizzes/9010', 'it is sent on to the quiz too');
const pdf = await same('the syllabus (a PDF, latin1 bytes through the Buffer stand-in)', 'GET', '/files/f1/download?download_frd=1');
check(pdf.got.bytes.subarray(0, 8).toString('latin1') === '%PDF-1.4' && pdf.got.headers['content-length'] === String(pdf.got.bytes.length), 'it is a PDF, its length said');
await same('a picture', 'GET', '/files/f5/download?download_frd=1&verifier=abc');
await same('an equation', 'GET', '/equation_images/%5Cvec%7Bv%7D');

console.log('writes, and state from one request to the next');
await same('a write without the CSRF token', 'POST', '/api/v1/planner_notes', { ...API, 'content-type': 'application/json' }, JSON.stringify({ title: 'Refused' }));
const note = await same('a note made', 'POST', '/api/v1/planner_notes', WRITE, JSON.stringify({ title: 'Read chapter 5', todo_date: '2026-10-20T12:00:00Z' }));
check(note.json().title === 'Read chapter 5', 'the note is made');
const notes = await same('the notes', 'GET', '/api/v1/planner_notes', API);
check(notes.json().some((n) => n.title === 'Read chapter 5'), 'the note made is listed after (the page keeps its state)');
await same('a discussion marked read', 'PUT', '/api/v1/courses/101/discussion_topics/7003/read_all', WRITE);
await same('a comment on a hand-in', 'PUT', '/api/v1/courses/104/assignments/4001/submissions/self', WRITE, JSON.stringify({ comment: { text_comment: 'Thanks — with a dash and “quotes”.' } }));
const pre = await same("a file hand-in's first step", 'POST', '/api/v1/courses/104/assignments/4002/submissions/self/files', WRITE, JSON.stringify({ name: 'essay.pdf', size: 12, content_type: 'application/pdf' }));
check(String(pre.json().upload_url || '').startsWith(`${ORIGIN}/__upload/`), `the file goes to the test school's own address (${pre.json().upload_url})`);
const boundary = '----simpl';
const multipart = `--${boundary}\r\nContent-Disposition: form-data; name="key"\r\n\r\nsubmissions/x\r\n--${boundary}\r\nContent-Disposition: form-data; name="file"; filename="essay.pdf"\r\nContent-Type: application/pdf\r\n\r\n%PDF-1.4 essay\r\n--${boundary}--\r\n`;
const uploadPath = String(pre.json().upload_url).slice(ORIGIN.length);
const stored = await same('the file sent', 'POST', uploadPath, { 'content-type': `multipart/form-data; boundary=${boundary}` }, multipart);
check(stored.json().display_name === 'essay.pdf', 'the file is kept under its name');
const handed = await same('the hand-in', 'POST', '/api/v1/courses/104/assignments/4002/submissions', WRITE, JSON.stringify({ submission: { submission_type: 'online_upload', file_ids: [stored.json().id] } }));
check(handed.json().workflow_state === 'submitted', 'the work is handed in');
const quiz = await same('a quiz begun', 'POST', '/api/v1/courses/101/quizzes/9011/submissions', WRITE, JSON.stringify({}));
check(quiz.json().quiz_submissions?.[0]?.workflow_state === 'untaken', 'the attempt is open');
await same("the quiz's own take page, now an attempt is open", 'GET', '/courses/101/quizzes/9011/take');

console.log("the test school's sign-in page");
const login = await page.evaluate(() => window.__mockLoginPage('Invalid username or password.', 'Tes<ter'));
check(login.includes('Simpl Test School') && !login.includes('Mock University'), 'it is the test school\'s');
check(login.includes('role="alert">Invalid username or password.</div>'), 'the error is Canvas\'s own, where Canvas shows it');
check(login.includes('value="Tes&lt;ter"'), 'the username typed is kept, escaped');
check(login.includes('id="pseudonym_session_unique_id"') && login.includes('id="pseudonym_session_password"') && login.includes('action="/login/canvas"'), 'the form is Canvas\'s own (what the sign-in reader and its form look for)');
check(!/demo/i.test(login), 'no password is shown on it');

// ---- the interface over it, as the app runs it: every request of the page answered by the hidden page ---------------
// (at http://test.simpl.com here, the app's scheme being the app's own; no cookie comes with any answer, as none is kept
// at that scheme — so a write must find the session's CSRF token in the page, as on the phone)
console.log('the interface on the test school');
const { shellScript, nativeStub } = appBundle({ root, host: 'test.simpl.com' });
const shellCtx = await browser.newContext({ viewport: { width: 402, height: 874 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, timezoneId: Intl.DateTimeFormat().resolvedOptions().timeZone });
await shellCtx.addInitScript(nativeStub);
await shellCtx.addInitScript(shellScript);
let asked = 0;
await shellCtx.route('http://test.simpl.com/**', async (route) => {
  const r = route.request();
  const u = new URL(r.url());
  asked++;
  try {
    const a = await viaApp(r.method(), u.pathname + u.search, r.headers(), (r.postDataBuffer() || Buffer.alloc(0)).toString('utf8'));
    await route.fulfill({ status: a.status, headers: a.headers, body: a.bytes });
  } catch { await route.abort().catch(() => {}); } // (the run over, the page gone)
});
const sp = await shellCtx.newPage();
const shellErrors = [];
sp.on('pageerror', (e) => shellErrors.push(e.message));
const nc = (name, args) => sp.evaluate(([n, a]) => self.BCVNative.call(n, a), [name, args || {}]);
await sp.goto('http://test.simpl.com/');
const settled = await sp.waitForFunction(() => document.documentElement.classList.contains('bcv-settled') && !!self.BCVNative, null, { timeout: 30000 }).then(() => true).catch(() => false);
const state = settled ? await sp.evaluate(() => self.__nativeCalls.filter((c) => c.op === 'shell.state').pop()) : null;
check(settled && state?.shell === true && state?.signedIn === true, `the interface comes up on it and tells the app it is ready, signed in (${asked} requests answered)`);
check(!(await sp.evaluate(() => document.cookie)).includes('_csrf_token'), 'no CSRF cookie in the page (none is kept at the app\'s scheme)');
const today = settled ? await nc('today') : null;
check(!!today && !today.error && today.list?.rows?.length > 0 && today.me?.name === 'Sam Student', `Today for the app: ${JSON.stringify({ rows: today?.list?.rows?.length, me: today?.me?.name, error: today?.error })}`);
const added = settled ? await nc('addTask', { title: 'Bring the lab manual' }) : null;
check(added?.ok === true && added.added === 1, `a write (a task added: a planner note) is accepted, its CSRF token found in the page: ${JSON.stringify(added)}`);
const todo = settled ? await nc('todo', { group: 'date' }) : null;
const row = todo?.sections?.flatMap((s) => s.rows).find((x) => x.id);
const ticked = row ? await nc('complete', { id: row.id, done: true }) : null;
check(ticked?.ok === true, `a row ticked is marked done on the planner: ${JSON.stringify(ticked)}`);
check(shellErrors.length === 0, `the interface ran without an error${shellErrors.length ? `: ${shellErrors.join(' | ')}` : ''}`);

check(pageErrors.length === 0, `the hidden page ran without an error${pageErrors.length ? `: ${pageErrors.join(' | ')}` : ''}`);

await shellCtx.unrouteAll({ behavior: 'ignoreErrors' });
await shellCtx.close();
await browser.close();
node.kill();
console.log(failures.length ? `\n${failures.length} failed` : '\nall passed');
process.exit(failures.length ? 1 : 0);

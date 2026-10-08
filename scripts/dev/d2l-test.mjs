#!/usr/bin/env node
// Brightspace (D2L): the interface on a made-up student's Brightspace (scripts/dev/mock-brightspace.mjs).
//
// First the layer on its own: lib/lms.js (which platform, and the addresses either way), lib/canvas-api.js and
// lib/d2l-api.js run in a sandbox whose fetch goes to the mock — every Canvas request the screens make is asked of it
// and the answers are checked in Canvas's shapes: courses and their terms, work of every kind and where it stands
// (graded, handed in, late, missing), the categories, content with a module inside a module, pages and files, the
// planner, announcements read and unread, discussions and a reply, a hand-in of a file and of text as Brightspace
// takes them (multipart/mixed, the token on every write), what is kept on the device (nicknames, colours, tasks).
//
// Then the browser: the extension loaded in Chromium on the mock — the interface over Brightspace's homepage and a
// course's, its screens at addresses Brightspace keeps, a hand-in from the assignment's page, a reply, Brightspace's
// own pages (a quiz, the sign-in) left as they are, a Canvas-style address sent on from Brightspace's 404.
//
// Then the apps: the interface loaded as the iPhone app and Simpl for Mac load it (app-bundle.mjs), their own chrome on —
// each native screen's call (content/app/native-app.js) answered from Brightspace, a hand-in and a reply from the app,
// and what the app's new-activity check needs to read Brightspace by itself.
//   node scripts/dev/d2l-test.mjs
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, rmSync, writeFileSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { tmpdir } from 'node:os';
import vm from 'node:vm';
import { launchExtension, shortenTimers, afterMigration } from './harness.mjs';
import { appBundle } from './app-bundle.mjs';

const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const PORT = Number(process.env.D2L_PORT) || 8861;
const BASE = `http://localhost:${PORT}`;
const out = join(root, 'scripts', 'dev', 'out', 'd2l');
mkdirSync(out, { recursive: true });

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };
const DAY = 864e5;

const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-brightspace.mjs'), String(PORT)], { stdio: 'ignore' });
process.on('exit', () => server.kill()); // (however the run ends)
for (let i = 0; i < 50; i++) { try { await fetch(`${BASE}/d2l/login`); break; } catch { await new Promise((r) => setTimeout(r, 100)); } }
const mockLog = async () => (await fetch(`${BASE}/__mock/log`)).json();

// ---- the layer, in a sandbox ------------------------------------------------------------------------------------------
function sandbox({ path = '/d2l/home', search = '', signedIn = true, ctx = { orgUnitId: '6606', orgId: '6606', userId: '7001' } } = {}) {
  const store = new Map();
  const classes = new Set();
  const replaced = [];
  const local = {};
  const box = {
    console, setTimeout, clearTimeout, URL, URLSearchParams, Blob, FormData, File, AbortController, TextEncoder, TextDecoder, Promise, JSON, Date, Math, Map, Set, Array, Object, String, Number, Error, Symbol, RegExp,
    location: { hostname: 'localhost', host: `localhost:${PORT}`, origin: BASE, pathname: path, search, hash: '', href: `${BASE}${path}${search}`, replace: (u) => replaced.push(u) },
    document: { cookie: '', querySelector: () => null, querySelectorAll: () => [], addEventListener: () => {}, documentElement: { getAttribute: (k) => (k === 'data-global-context' && signedIn ? JSON.stringify(ctx) : null), classList: { add: (c) => classes.add(c), contains: (c) => classes.has(c) } } },
    localStorage: { getItem: (k) => (store.has(k) ? store.get(k) : null), setItem: (k, v) => store.set(k, String(v)), removeItem: (k) => store.delete(k) },
    fetch: (u, init = {}) => fetch(new URL(u, BASE), { ...init, headers: { ...(init.headers || {}), ...(signedIn ? { cookie: 'd2lSessionVal=ok' } : {}) } }),
  };
  box.self = box;
  box.window = box;
  box.open = (u) => { box.opened = u; return null; };
  box.top = box;
  box.BCV = { api: { storage: { local: { get: async (k) => (k in local ? { [k]: JSON.parse(JSON.stringify(local[k])) } : {}), set: async (o) => { for (const [k, v] of Object.entries(o)) local[k] = JSON.parse(JSON.stringify(v)); } } } } };
  vm.createContext(box);
  for (const f of ['lib/lms.js', 'lib/canvas-api.js', 'lib/d2l-api.js']) vm.runInContext(readFileSync(join(root, 'extension', f), 'utf8'), box, { filename: f });
  return { box, BCV: box.BCV, classes, replaced, local };
}

const sb = sandbox();
const { BCV } = sb;
// (the store's own doors: lib/canvas-api.js get / post / put / del)
const C = (method, path, { params, body } = {}) => (method === 'GET' ? BCV.canvas.get(path, { params }) : method === 'POST' ? BCV.canvas.post(path, body) : method === 'PUT' ? BCV.canvas.put(path, body) : BCV.canvas.del(path));
const get = (path, params) => C('GET', path, { params });

console.log('which platform');
check(BCV.lms.kind === 'd2l' && BCV.lms.name === 'Brightspace' && sb.classes.has('bcv-d2l') && !!BCV.d2l, 'a page under /d2l/ is Brightspace’s: the platform says so, the page is marked, the Brightspace layer is up');
{
  const canvas = (() => { const b = sandbox({ path: '/courses/101', signedIn: false }); return b; })();
  check(canvas.BCV.lms.kind === 'canvas' && !canvas.BCV.d2l && !canvas.classes.has('bcv-d2l') && canvas.BCV.lms.toPage('/courses/101/assignments') === '/courses/101/assignments', 'anywhere else it is Canvas’s, with no Brightspace layer and the addresses as they are');
}

console.log('\naddresses, either way');
{
  const L = BCV.lms;
  const pairs = [
    ['/', '/d2l/home'],
    ['/courses/31001', '/d2l/home/31001'],
    ['/courses/31001/assignments', '/d2l/home/31001?simpl=%2Fcourses%2F31001%2Fassignments'],
    ['/courses/31001/assignments/702', '/d2l/home/31001?simpl=%2Fcourses%2F31001%2Fassignments%2F702'],
    ['/courses/31001/discussion_topics/2000000902', '/d2l/home/31001?simpl=%2Fcourses%2F31001%2Fdiscussion_topics%2F2000000902'],
    ['/calendar', '/d2l/home?simpl=%2Fcalendar'],
    ['/grades', '/d2l/home?simpl=%2Fgrades'],
  ];
  const bad = pairs.filter(([c, d]) => L.toPage(c) !== d || L.fromPage(d) !== c);
  check(!bad.length, `each screen the interface draws has a Brightspace address that always loads (the homepage, or the course’s, with ?simpl=), and back: ${bad.map(([c, d]) => `${c} → ${L.toPage(c)} (want ${d}) → ${L.fromPage(d)}`).join('; ') || pairs.length + ' pairs'}`);
  check(L.toPage('/#todo') === '/d2l/home#todo' && L.fromPage('/d2l/home#todo') === '/#todo' && L.toPage('/courses?view=past') === '/d2l/home?simpl=%2Fcourses%3Fview%3Dpast', `a screen of the homepage keeps its hash, a query of the interface’s own rides in ?simpl=: ${L.toPage('/#todo')} · ${L.toPage('/courses?view=past')}`);
  check(L.toPage('/courses/31001/quizzes/802') === '/d2l/lms/quizzing/user/quiz_summary.d2l?qi=802&ou=31001' && L.toPage('/courses/31001/assignments/1000000802') === '/d2l/lms/quizzing/user/quiz_summary.d2l?qi=802&ou=31001' && L.fromPage('/d2l/lms/quizzing/user/quiz_summary.d2l?qi=802&ou=31001') === '/courses/31001/quizzes/802?bcv=native', 'a quiz — by its own id or its assignment’s — is Brightspace’s own quiz page, which the interface leaves as it is');
  check(L.toPage('/courses/31001/modules/items/1308') === '/d2l/le/content/31001/viewContent/1308/View' && L.fromPage('/d2l/le/content/31001/viewContent/1308/View') === '/courses/31001/modules/items/1308?bcv=native', 'a content topic the interface does not draw is Brightspace’s content viewer, left as it is');
  check(L.fromPage('/d2l/lms/dropbox/user/folder_submit_files.d2l?db=702&ou=31001') === '/courses/31001/assignments/702' && L.fromPage('/d2l/le/content/31001/Home') === '/courses/31001/modules' && L.fromPage('/d2l/lms/grades/my_grades/main.d2l?ou=31001') === '/courses/31001/grades' && L.fromPage('/d2l/le/31001/discussions/topics/902/View') === '/courses/31001/discussion_topics/2000000902', 'Brightspace’s own pages for an assignment, the content, the grades and a discussion are the interface’s screens for them');
  const nat = [
    ['/courses/31001/assignments/702?bcv=native', '/d2l/lms/dropbox/user/folder_submit_files.d2l?db=702&ou=31001&bcv=native'],
    ['/courses/31001/grades?bcv=native', '/d2l/lms/grades/my_grades/main.d2l?ou=31001&bcv=native'],
    ['/courses/31001/discussion_topics/2000000902?bcv=native', '/d2l/le/31001/discussions/topics/902/View?bcv=native'],
    ['/courses/31001/modules?bcv=native', '/d2l/le/content/31001/Home?bcv=native'],
  ];
  nat.push(['/courses/31001?bcv=native', '/d2l/home/31001?bcv=native'], ['/?bcv=native', '/d2l/home?bcv=native']); // (the pages the interface draws over: asked for as Brightspace's own, they say so)
  const natBad = nat.filter(([c, d]) => L.toPage(c) !== d || L.fromPage(d) !== c);
  check(!natBad.length && L.toPage('/courses/31001/files/1302?bcv=native') === '/d2l/le/content/31001/viewContent/1302/View', `Brightspace’s own page for a screen, when its own is asked for (“Open in Brightspace”), carries ?bcv=native so the interface leaves it be — and back: ${natBad.map(([c, d]) => `${c} → ${L.toPage(c)} → ${L.fromPage(L.toPage(c))}`).join('; ') || nat.length + ' pairs'}`);
  check(L.toPage('/profile') === '/d2l/lp/profile/profile_edit.d2l?ou=6606' && L.toPage('/profile/settings') === '/d2l/lp/preferences/preferences_main/preferences_main.d2l?ou=6606' && L.toPage('/profile/communication') === '/d2l/Notifications/Settings?ou=6606' && L.toPage('/logout') === '/d2l/logout', 'the account’s pages — profile, settings, notifications — and signing out are Brightspace’s own, as its personal menu links them');
  check(L.fromPage('/d2l/lp/preferences/preferences_main.d2l?ou=6606') === null && L.toPage('https://example.org/x') === 'https://example.org/x' && L.toPage('/d2l/home/31001') === '/d2l/home/31001', 'a Brightspace page the interface has no screen for is left to Brightspace; another site’s address and a Brightspace one pass through');
  const edit = (raw) => { const u = L.routeUrl(raw); u.searchParams.delete('bcv'); u.searchParams.delete('step'); return L.pageFor(u); };
  check(edit(`${BASE}/d2l/home?simpl=${encodeURIComponent('/?bcv=setup')}`) === '/d2l/home' && edit(`${BASE}/d2l/home/31001?simpl=${encodeURIComponent('/courses/31001/grades?bcv=whatsnew')}`) === '/d2l/home/31001?simpl=%2Fcourses%2F31001%2Fgrades' && edit(`${BASE}/d2l/home?simpl=${encodeURIComponent('/courses?bcv=setup&step=theme&view=past')}`) === '/d2l/home?simpl=%2Fcourses%3Fview%3Dpast', 'the interface’s own query (?bcv=setup, ?bcv=whatsnew) is edited on its own address, inside ?simpl=, and the page address made again');
  const cv = sandbox({ path: '/courses/101', search: '?bcv=setup&step=theme', signedIn: false });
  const cu = cv.BCV.lms.routeUrl();
  cu.searchParams.delete('bcv');
  check(cv.BCV.lms.pageFor(cu) === '/courses/101?step=theme', 'on Canvas the same edit is the page’s own address, as before');
  sb.box.open('/courses/31001/grades', '_blank');
  const viaOpen = sb.box.opened;
  sb.box.open('https://example.org/x', '_blank');
  check(viaOpen === `${BASE}/d2l/home/31001?simpl=%2Fcourses%2F31001%2Fgrades` && sb.box.opened === 'https://example.org/x', `the interface’s own window.open of one of its addresses opens the Brightspace page for it; another site’s as it is: ${viaOpen}`);
  const login = sandbox({ path: '/d2l/login', signedIn: false });
  check(login.BCV.lms.isLoginPage() && !BCV.lms.isLoginPage(), 'Brightspace’s sign-in page is known for what it is');
  const lost = sandbox({ path: '/d2l/error/404/log', search: `?targetUrl=${encodeURIComponent('/courses/31001/assignments/702')}` });
  check(lost.replaced.join() === '/d2l/home/31001?simpl=%2Fcourses%2F31001%2Fassignments%2F702', `a Canvas-style address that reached Brightspace’s 404 is sent on at once to the page it means: ${lost.replaced.join()}`);
}

console.log('\nthe student and the courses');
{
  const v = await BCV.d2l.versions();
  check(v.lp === '1.50' && v.le === '1.82', `the API versions it was written against, which the school supports: ${JSON.stringify(v)}`);
  const me = await get('/api/v1/users/self');
  check(me.id === '7001' && me.name === 'Avery Quinn' && me.short_name === 'Avery' && me.sortable_name === 'Quinn, Avery' && me.email === 'avery.quinn@lakeside.example', `who is signed in: ${me.name} (${me.id})`);
  const cs = await get('/api/v1/courses', { include: ['term', 'total_scores'] });
  const ids = cs.map((c) => c.id).sort().join(',');
  check(ids === '30990,31001,31002,31003', `every course offering on both pages of the enrollments — not the organization, not the one the student can no longer open: ${ids}`);
  const bio = cs.find((c) => c.id === '31001');
  check(bio.term?.name === 'Fall 2026' && cs.find((c) => c.id === '30990').term?.name === 'Spring 2026' && bio.is_favorite && !cs.find((c) => c.id === '31003').is_favorite, 'each with its semester as its term, pinned courses as the favourites');
  check(bio.apply_assignment_group_weights === true && cs.find((c) => c.id === '31002').apply_assignment_group_weights === false, 'whether a course’s categories are weighted comes with the list (Brightspace’s grading system), as the Grades screen reads it');
  check(bio.enrollments[0].computed_current_score === 87.5 && bio.enrollments[0].computed_current_grade === 'B+' && bio.enrollments[0].type === 'student', `the course total from Brightspace’s calculated final grade: ${bio.enrollments[0].computed_current_score}% ${bio.enrollments[0].computed_current_grade}`);
  const eng = cs.find((c) => c.id === '30990');
  check(eng.restrict_enrollments_to_course_dates && new Date(eng.end_at) < new Date(), 'a course whose dates have passed says so (the interface files it under Past)');
  const one = await get('/api/v1/courses/31001', { include: ['teachers', 'syllabus_body'] });
  check(one.apply_assignment_group_weights === true && one.teachers.map((t) => t.display_name).join() === 'Lena Ortiz', `one course: weighted categories (Brightspace’s weighted grading system), its instructor from the classlist: ${one.teachers.map((t) => t.display_name)}`);
  const tabs = await get('/api/v1/courses/31001/tabs');
  check(tabs.map((t) => t.label).join(' | ') === 'Home | Announcements | Assignments | Discussions | Grades | Classlist | Quizzes | Content', `its sections, in Brightspace’s words: ${tabs.map((t) => t.label).join(' | ')}`);
}

console.log('\nwork, and where it stands');
{
  const all = await get('/api/v1/courses/31001/assignments', { include: ['submission'] });
  const by = Object.fromEntries(all.map((a) => [a.id, a]));
  check(['701', '702', '703', '704', '705', '1000000801', '1000000802', '2000000902', '4000000504'].every((id) => by[id]) && all.length === 9, `every piece of work, one list: the dropbox folders, the quizzes, the graded discussion, the grade item no tool owns — not the hidden folder, the hidden grade item, nor a grade item a tool already lists (${all.length}: ${all.map((a) => a.name).join(', ')})`);
  check(all.every((a, i) => i === 0 || (new Date(a.due_at || 8.64e15) >= new Date(all[i - 1].due_at || 8.64e15))), 'in the order it is due, undated last');
  const lab1 = by['701'].submission;
  check(lab1.workflow_state === 'graded' && lab1.score === 18 && !lab1.late && !lab1.missing && lab1.attachments[0]?.display_name === 'microscopy.pdf' && /nucleolus/.test(lab1.submission_comments.map((c) => c.comment).join(' ')), `handed in on time and graded, with its file and the instructor’s comments: ${lab1.workflow_state} ${lab1.score}/${by['701'].points_possible}, ${lab1.attachments.map((f) => f.display_name)}`);
  check(by['702'].submission.workflow_state === 'unsubmitted' && !by['702'].submission.missing && by['702'].submission_types.join() === 'online_upload' && by['702'].points_possible === 20, 'due in two days, not yet handed in: open, a file upload, 20 points');
  check(by['704'].submission.missing === true && by['703'].submission_types.join() === 'online_text_entry' && by['705'].submission_types.join() === 'on_paper', 'a past due date with nothing handed in is missing; a text entry and an on-paper folder say so');
  check(by['1000000801'].submission.workflow_state === 'graded' && !!by['1000000801'].submission.submitted_at && by['1000000801'].submission.submission_type === 'online_quiz' && !by['1000000801'].submission.late && !by['4000000504'].submission.submitted_at, 'a quiz with a grade was taken (the grade’s time stands for when, and says nothing of late); a grade item of its own was handed nothing');
  check(by['1000000801'].submission.score === 41 && by['1000000801'].submission_types.join() === 'online_quiz' && by['1000000802'].allowed_attempts === 2 && by['2000000902'].submission_types.join() === 'discussion_topic' && by['2000000902'].points_possible === 10 && by['4000000504'].submission.score === 9, 'a quiz with its score from the grades, one with two attempts, the graded discussion, the grade item of its own with its score');
  const one = await get('/api/v1/courses/31001/assignments/702', { include: ['submission'] });
  check(one.name === 'Lab 2: Osmosis' && /data table/.test(one.description) && one.can_submit === true, 'one folder as an assignment: its instructions, and it can be handed in');
  const mathA = (await get('/api/v1/courses/31003/assignments'))[0];
  check(mathA.allowed_extensions.join() === 'pdf,png', `a folder that takes only some files names them: ${mathA.allowed_extensions}`);
  const groups = await get('/api/v1/courses/31001/assignment_groups', { include: ['assignments', 'submission'] });
  check(groups.map((g) => `${g.name}:${g.group_weight}:${g.assignments.length}`).join(' | ') === 'Labs:40:4 | Exams:60:1', `the categories with their weights and their work: ${groups.map((g) => `${g.name}:${g.group_weight}:${g.assignments.map((a) => a.name).join('+')}`).join(' | ')}`);
}

console.log('\ncontent');
{
  const mods = await get('/api/v1/courses/31001/modules', { include: ['items', 'content_details'] });
  check(mods.map((m) => m.name).join(' | ') === 'Week 1: The Cell | Week 2: Membranes', `the modules, the hidden one left out: ${mods.map((m) => m.name).join(' | ')}`);
  const w1 = mods[0].items.map((it) => `${it.type}:${it.title}:${it.indent}`);
  check(w1.join(' | ') === 'Page:Syllabus:0 | File:Cell Diagram:0 | Assignment:Lab 1: Microscopy:0 | SubHeader:Optional Reading:0 | ExternalUrl:Cell Biology Primer:1 | Discussion:Introduce Yourself:0', `a module inside a module is a heading with its topics a step in, everything in Brightspace’s order: ${w1.join(' | ')}`);
  const [, diagram, lab, , primer, intro] = mods[0].items;
  check(lab.content_id === '701' && intro.content_id === '2000000901' && primer.external_url === 'https://example.org/primer' && diagram.content_id === '1302' && mods[0].items[0].page_url === '1301', 'each topic leads to its own: the folder, the discussion, the link, the file, the page');
  const w2 = mods[1].items;
  check(w2[1].type === 'Quiz' && w2[1].content_id === '802' && w2[2].type === 'ExternalUrl' && w2[2].external_url === `${BASE}/d2l/le/content/31001/viewContent/1308/View` && !w2[2].completion_requirement && w2[0].completion_requirement?.type === 'must_view', 'a quiz; a checklist (Brightspace’s to show) opens in its content viewer; view-to-complete, and nothing asked of a topic that asks nothing');
  let refusedMark = null;
  try { await BCV.canvas.post('/api/v1/courses/31001/modules/1201/items/1301/done'); } catch (e) { refusedMark = e; }
  check(mods[0].items[0].completion_requirement?.type === 'must_mark_done' && refusedMark?.status === 400 && /own page/.test(refusedMark.message), `a topic done by hand asks for the mark, which Brightspace takes on its own page only: ${refusedMark?.message}`);
  const page = await get('/api/v1/courses/31001/pages/1301');
  check(page.title === 'Syllabus' && /BIO 110 Syllabus/.test(page.body) && !/<html|<body/i.test(page.body), 'an HTML topic is a page: its body, read with the session');
  const file = await get('/api/v1/files/1302');
  check(file['content-type'] === 'application/pdf' && file.display_name === 'Cell Diagram' && file.url === '/d2l/api/le/1.82/31001/content/topics/1302/file?stream=false' && file.mime_class === 'pdf' && file.preview_url === '/d2l/le/content/31001/fullscreen/1302/View?skipHeader=True', `a file topic is a file, fetched from the topic, previewed in Brightspace’s own viewer where it is not drawn here: ${file.url}`);
  const seq = await get('/api/v1/courses/31001/module_item_sequence', { asset_type: 'ModuleItem', asset_id: '1302' });
  check(seq.items[0].prev?.title === 'Syllabus' && seq.items[0].next?.title === 'Lab 1: Microscopy', 'Previous and Next run through the topics in reading order');
}

console.log('\nannouncements, discussions');
{
  const ann = await get('/api/v1/announcements', { 'context_codes[]': ['course_31001', 'course_31002'] });
  check(ann.map((a) => a.title).join(' | ') === 'Lab 2 moved to Thursday | Reading for Monday | Welcome to BIO 110' && ann.every((a) => a.read_state === 'unread' && /^3\d{9}$/.test(a.id)), `every course’s announcements, newest first, unread: ${ann.map((a) => a.title).join(' | ')}`);
  const one = await get('/api/v1/courses/31001/discussion_topics/3000001102');
  const again = await get('/api/v1/announcements', { 'context_codes[]': ['course_31001'] });
  check(/Thursday/.test(one.message) && again.find((a) => a.id === '3000001102').read_state === 'read' && !!sb.local['d2l:localhost:' + PORT]?.read?.['news:1102'], 'opening one marks it read, on this device');
  const topics = await get('/api/v1/courses/31001/discussion_topics');
  check(topics.map((t) => t.title).join(' | ') === 'Introduce Yourself | Week 3 Discussion' && topics[1].assignment?.points_possible === 10 && topics[1].unread_count === 2 && topics[1].author.display_name === 'Weekly Discussions', 'the topics: the graded one with its points and unread posts, under its forum’s name');
  const view = await get('/api/v1/courses/31001/discussion_topics/2000000902/view');
  check(view.view.length === 1 && view.view[0].replies.length === 1 && view.participants.length === 2 && /mule/.test(view.view[0].replies[0].message), 'the posts as threads: a post and the reply to it, the people in them');
  await fetch(`${BASE}/__mock/reset`);
  const posted = await C('POST', '/api/v1/courses/31001/discussion_topics/2000000902/entries', { body: { message: '<p>Alive enough to evolve.</p>' } });
  const replied = await C('POST', '/api/v1/courses/31001/discussion_topics/2000000902/entries/5001/replies', { body: { message: '<p>Agreed.</p>' } });
  const log = (await mockLog()).filter((e) => e.kind === 'post');
  check(log.length === 2 && log[0].parent === null && log[0].html === '<p>Alive enough to evolve.</p>' && log[1].parent === 5001 && !!posted.id && replied.parent_id === '5001', `a post and a reply are posted to Brightspace, with the page’s token: ${JSON.stringify(log.map((e) => ({ parent: e.parent, subject: e.subject })))}`);
  const after = await get('/api/v1/courses/31001/discussion_topics/2000000902/view');
  check(after.view.length === 2 && after.view[0].replies.length === 2, 'and they are there when the topic is read again');
  let refused = null;
  try { await C('POST', '/api/v1/courses/31001/discussion_topics/3000001102/entries', { body: { message: 'x' } }); } catch (e) { refused = e; }
  check(refused?.status === 400, 'an announcement takes no replies here');
}

console.log('\nhanding in');
{
  await fetch(`${BASE}/__mock/reset`);
  const start = await C('POST', '/api/v1/courses/31001/assignments/702/submissions/self/files', { body: { name: 'osmosis.pdf', size: 12, content_type: 'application/pdf' } });
  const form = new FormData();
  form.append('file', new File(['%PDF-1.4 data'], 'osmosis.pdf', { type: 'application/pdf' }));
  const up = await BCV.canvas.upload(start.upload_url, form);
  const sub = await C('POST', '/api/v1/courses/31001/assignments/702/submissions', { body: { submission: { submission_type: 'online_upload', file_ids: [up.id] }, comment: { text_comment: 'Data attached' } } });
  const [s1] = (await mockLog()).filter((e) => e.kind === 'submit');
  check(/^d2l-upload:/.test(start.upload_url) && s1?.folder === 702 && s1.comment.Text === 'Data attached' && s1.files.length === 1 && s1.files[0].name === 'osmosis.pdf' && s1.files[0].type === 'application/pdf' && s1.files[0].size === 13, `a file is handed in to the folder as Brightspace takes it — the comment, then the file, one multipart/mixed post: ${JSON.stringify(s1 && { comment: s1.comment, files: s1.files.map((f) => f.name) })}`);
  check(sub.workflow_state === 'submitted' && !!sub.submitted_at && sub.attachments[0]?.display_name === 'osmosis.pdf', `and Brightspace’s answer is read back as the submission: ${sub.workflow_state}, ${sub.attachments.map((f) => f.display_name)}`);
  const back = sub.attachments[0]?.url ? await fetch(new URL(sub.attachments[0].url, BASE), { headers: { cookie: 'd2lSessionVal=ok' } }) : null;
  check(/^\/d2l\/api\/le\/1\.82\/31001\/dropbox\/folders\/702\/submissions\/\d+\/files\/\d+$/.test(sub.attachments[0]?.url || '') && back?.ok && (await back.text()) === '%PDF-1.4 data', `each file handed in is fetched back from the folder’s submission, as it went (the apps list it and open it): ${sub.attachments[0]?.url}`);
  await C('POST', '/api/v1/courses/31001/assignments/703/submissions', { body: { submission: { submission_type: 'online_text_entry', body: '<p>Mitochondria have their own DNA.</p>' } } });
  const s2 = (await mockLog()).filter((e) => e.kind === 'submit')[1];
  check(s2?.folder === 703 && s2.files[0].name === 'Text Submission.html' && /Mitochondria have their own DNA/.test(s2.files[0].text || ''), 'text is handed in as a page of its own, the way Brightspace keeps a text submission');
  let refused = null;
  try { await C('POST', '/api/v1/courses/31001/assignments/1000000802/submissions', { body: { submission: { submission_type: 'online_upload', file_ids: [] } } }); } catch (e) { refused = e; }
  check(refused?.status === 400 && /Brightspace/.test(refused.message), `a quiz is not handed in here: ${refused?.message}`);
}

console.log('\nthe planner, and what stays on this device');
{
  const from = new Date(Date.now() - 7 * DAY).toISOString(), to = new Date(Date.now() + 14 * DAY).toISOString();
  const items = await get('/api/v1/planner/items', { start_date: from, end_date: to });
  const titles = items.map((i) => `${i.plannable_type}:${i.plannable.title}`);
  check(['assignment:Lab 2: Osmosis', 'quiz:Chapter 4 Check', 'discussion_topic:Week 3 Discussion', 'calendar_event:Lab office hours', 'announcement:Lab 2 moved to Thursday', 'assignment:Essay Proposal'].every((t) => titles.includes(t)) && !titles.some((t) => /Lab 2: Osmosis - Due/.test(t)), `what is due, what is on, what was said — every course, a due date’s own calendar entry left out: ${titles.join(' · ')}`);
  const lab2 = items.find((i) => i.plannable.title === 'Lab 2: Osmosis');
  check(lab2.submissions.submitted === true, 'the work just handed in counts as handed in');
  const note = await C('POST', '/api/v1/planner_notes', { body: { title: 'Review chapter 4', todo_date: new Date(Date.now() + DAY).toISOString(), course_id: '31001' } });
  const ov = await C('POST', '/api/v1/planner/overrides', { body: { plannable_type: 'quiz', plannable_id: '1000000802', marked_complete: true } });
  const again = await get('/api/v1/planner/items', { start_date: from, end_date: to });
  check(again.some((i) => i.plannable_type === 'planner_note' && i.plannable.title === 'Review chapter 4') && again.find((i) => i.plannable.title === 'Chapter 4 Check')?.planner_override?.marked_complete === true, 'a task of the student’s own, and a tick on a quiz, are kept on this device and come back with the planner');
  await C('DELETE', `/api/v1/planner_notes/${note.id}`);
  await C('PUT', `/api/v1/planner/overrides/${ov.id}`, { body: { marked_complete: false } });
  const third = await get('/api/v1/planner/items', { start_date: from, end_date: to });
  check(!third.some((i) => i.plannable_type === 'planner_note') && third.find((i) => i.plannable.title === 'Chapter 4 Check')?.planner_override?.marked_complete === false, 'and deleted, and unticked');
  await C('PUT', '/api/v1/users/self/course_nicknames/31003', { body: { nickname: 'Calc' } });
  await C('PUT', '/api/v1/users/self/colors/course_31001', { body: { hexcode: 'ff9500' } });
  const cs = await get('/api/v1/courses');
  const colors = await get('/api/v1/users/self/colors');
  check(cs.find((c) => c.id === '31003').name === 'Calc' && cs.find((c) => c.id === '31003').original_name === 'MATH 140: Calculus I' && colors.custom_colors.course_31001 === '#ff9500', 'a nickname and a colour, kept on this device, are the course’s name and colour from then on');
  await fetch(`${BASE}/__mock/reset`);
  await C('DELETE', '/api/v1/users/self/favorites/courses/31002');
  const pins = (await mockLog()).filter((e) => e.kind === 'pin');
  const after = await get('/api/v1/courses');
  check(pins.length === 1 && pins[0].ou === 31002 && pins[0].pinned === false && after.find((c) => c.id === '31002').is_favorite === false, 'a favourite is Brightspace’s pin: unstarring unpins the course there');
  await C('POST', '/api/v1/users/self/favorites/courses/31002');
}

console.log('\nthe rest');
{
  const ev = await get('/api/v1/calendar_events', { type: 'event', 'context_codes[]': ['course_31001'], start_date: new Date(Date.now() - DAY).toISOString(), end_date: new Date(Date.now() + 7 * DAY).toISOString() });
  const due = await get('/api/v1/calendar_events', { type: 'assignment', 'context_codes[]': ['course_31001'], start_date: new Date(Date.now() - DAY).toISOString(), end_date: new Date(Date.now() + 7 * DAY).toISOString() });
  check(ev.map((e) => e.title).join() === 'Lab office hours' && ev[0].location_name === 'Science Hall 204' && due.some((e) => e.title === 'Lab 2: Osmosis') && due.every((e) => e.type === 'assignment'), `the calendar: the course’s events, and its work by due date: ${ev.map((e) => e.title)} · ${due.map((e) => e.title).join(', ')}`);
  const people = await get('/api/v1/courses/31001/users', { include: ['enrollments'] });
  check(people.length === 4 && people.find((p) => p.name === 'Lena Ortiz').enrollments[0].type === 'TeacherEnrollment' && people.find((p) => p.name === 'Sam Patel').enrollments[0].type === 'StudentEnrollment', 'the classlist, instructors told from students');
  const qs = await get('/api/v1/courses/31001/quizzes');
  check(qs.length === 2 && qs[0].time_limit === 45 && qs[0].question_count === null && qs[0].allowed_attempts === 1 && qs[0].d2l.page === '/d2l/lms/quizzing/user/quiz_summary.d2l?qi=801&ou=31001', 'the quizzes, with their time limits and attempts — no question count, which Brightspace does not give a student');
  check((await get('/api/v1/conversations')).length === 0 && (await get('/api/v1/users/self/groups')).length === 0 && (await get('/api/v1/appointment_groups')).length === 0, 'what Brightspace has no such thing for answers empty: the inbox, groups, appointments');
  let unknown = null;
  try { await get('/api/v1/courses/31001/rubrics'); } catch (e) { unknown = e; }
  check(unknown?.status === 404 && /Not in Brightspace/.test(unknown.message), `anything else is a 404 that says so: ${unknown?.message}`);
  const out = sandbox({ signedIn: false });
  let gone = null;
  try { await out.BCV.canvas.get('/api/v1/users/self'); } catch (e) { gone = e; }
  let after = null;
  try { await out.BCV.canvas.get('/api/v1/courses'); } catch (e) { after = e; }
  check(gone?.status === 401 && after?.status === 401 && /Signed out of Brightspace/.test(after.message), 'signed out: the first refusal ends the session, and every request after it fails at once');
}

// ---- the browser ----------------------------------------------------------------------------------------------------------
console.log('\nin the browser');
const extDir = join(tmpdir(), `bcv-d2l-ext-${process.pid}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*') && cs.matches.includes('*://*.brightspace.com/*')) cs.matches.push(`${BASE}/*`);
manifest.host_permissions.push(`${BASE}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
shortenTimers(extDir);
const userDataDir = join(tmpdir(), `bcv-d2l-profile-${process.pid}`);
let context;
try {
  let sw;
  ({ context, sw } = await launchExtension(chromium, userDataDir, extDir, { viewport: { width: 1360, height: 900 } }));
  for (const p of context.pages()) if (p.url().includes('/setup/')) await p.close().catch(() => {});
  await afterMigration(sw); // (the background's first-run migration clears these flags if it runs after them)
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'tips:rubricRing': true, 'tools:welcomed': true, 'whatsnew:seen': v }), manifest.version);
  const page = await context.newPage();
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  const shot = (name) => page.screenshot({ path: join(out, `${name}.png`) }).catch(() => {});
  const drawn = () => page.waitForFunction(() => { const c = document.documentElement.classList; return c.contains('bcv-on') && c.contains('bcv-settled') && !!document.querySelector('#bcv-main'); }, null, { timeout: 20000 }).then(() => true, () => false);
  const mainText = () => page.evaluate(() => (document.querySelector('#bcv-main')?.innerText || '').replace(/\s+/g, ' '));
  const state = () => page.evaluate(() => ({ on: document.documentElement.classList.contains('bcv-on'), d2l: document.documentElement.classList.contains('bcv-d2l'), app: !!document.getElementById('bcv-app'), path: location.pathname + location.search + location.hash }));

  await page.goto(`${BASE}/d2l/login`);
  await page.waitForTimeout(1500);
  const atLogin = await state();
  check(!atLogin.on && (await page.$('#d2l_login')) !== null && (await page.isVisible('#userName')), `Brightspace’s sign-in page is left as it is: ${JSON.stringify(atLogin)}`);
  await page.fill('#userName', 'aquinn');
  await page.fill('#password', 'secret');
  await Promise.all([page.waitForURL(`${BASE}/d2l/home`, { timeout: 15000 }), page.click('#login')]);

  const homeDrawn = await drawn();
  const home = await state();
  const nav = await page.$$eval('.bcv-nav__item, [data-nav]', (els) => els.map((e) => (e.innerText || '').trim().split('\n')[0]).filter(Boolean)).catch(() => []);
  const dash = await mainText();
  check(homeDrawn && home.on && home.d2l && home.app && /Dashboard/.test(dash) && /BIO 110/.test(dash), `signed in, Brightspace’s homepage is the Dashboard: ${JSON.stringify(home)}`);
  const navText = await page.evaluate(() => (document.querySelector('#bcv-side, .bcv-side, nav')?.innerText || '').replace(/\s+/g, ' '));
  check(!/\bInbox\b/.test(navText) && !/\bGroups\b/.test(navText) && /To Do/.test(navText) && /Calendar/.test(navText), `the sidebar has no Inbox and no Groups (Brightspace has neither here): ${navText.slice(0, 160)}${nav.length ? '' : ''}`);
  await shot('dashboard');

  // a page done by hand: the mark is Brightspace's to take, on its own page for the topic
  await page.goto(`${BASE}/d2l/home/31001?simpl=${encodeURIComponent('/courses/31001/pages/1301')}`);
  await drawn();
  await page.waitForSelector('a:has-text("Mark done on Brightspace")', { timeout: 8000 }).catch(() => {});
  const markLink = await page.$eval('a:has-text("Mark done on Brightspace")', (a) => a.getAttribute('href')).catch(() => null);
  check(markLink === '/d2l/le/content/31001/viewContent/1301/View' && !(await page.$('button:has-text("Mark as done")')), `a page done by hand offers Brightspace’s own page for the mark, not a button it would refuse: ${markLink}`);

  await page.goto(`${BASE}/d2l/home/31001`);
  await drawn();
  const course = await mainText();
  check(/BIO 110: Cells and Systems/.test(course) && /Classlist/.test(course) && /Content/.test(course) && (await state()).path === '/d2l/home/31001', 'a course’s homepage is the course, with Brightspace’s section names');
  await shot('course');

  // a screen of the course, from its row: the address bar shows a Brightspace page that loads it again
  await page.click('text=Assignments');
  await page.waitForFunction(() => /simpl=%2Fcourses%2F31001%2Fassignments/.test(location.search), null, { timeout: 10000 }).catch(() => {});
  await page.waitForTimeout(800);
  const list = await mainText();
  check((await state()).path === '/d2l/home/31001?simpl=%2Fcourses%2F31001%2Fassignments' && /Lab 2: Osmosis/.test(list) && /Lab Safety Form/.test(list) && /Missing/i.test(list), `Assignments, in place, at an address Brightspace keeps: ${(await state()).path}`);
  await shot('assignments');
  await page.reload();
  await drawn();
  check(/Lab 2: Osmosis/.test(await mainText()), 'and reloaded there, it is the same screen');

  // the look turned off on a screen: Brightspace's own page for it, left as it is — and back on, the interface's screen
  await sw.evaluate(() => self.BCV.settings.update(self.BCV.settings.lookPatch(false)));
  await page.waitForURL((u) => u.pathname === '/d2l/lms/dropbox/user/folders_list.d2l', { timeout: 15000 }).catch(() => {});
  await page.waitForTimeout(1200);
  const off = await state();
  check(off.path === '/d2l/lms/dropbox/user/folders_list.d2l?ou=31001&bcv=native' && !off.on && (await page.isVisible('#d2l-folders')), `the look turned off on Assignments lands on Brightspace’s own assignments page: ${off.path}`);
  await sw.evaluate(() => self.BCV.settings.update(self.BCV.settings.lookPatch(true)));
  await page.waitForURL((u) => /simpl=/.test(u.search), { timeout: 15000 }).catch(() => {});
  await drawn();
  const on = await state();
  check(on.path === '/d2l/home/31001?simpl=%2Fcourses%2F31001%2Fassignments' && on.on && /Lab 2: Osmosis/.test(await mainText()), `and turned on again there, the interface’s Assignments: ${on.path}`);

  // a Canvas-style address typed (or opened from elsewhere): Brightspace's 404 names it, and it is sent on
  await page.goto(`${BASE}/courses/31001/grades`);
  await page.waitForURL((u) => u.pathname === '/d2l/home/31001' && /simpl=/.test(u.search), { timeout: 15000 }).catch(() => {});
  await drawn();
  const grades = await mainText();
  check((await state()).path === '/d2l/home/31001?simpl=%2Fcourses%2F31001%2Fgrades' && /87\.5|B\+/.test(grades) && /18\s*\/\s*20|18 \/ 20|18\/20/.test(grades), `a Canvas-style address lands on Brightspace’s 404 and is sent on to the grades: ${(await state()).path}`);
  await shot('grades');

  // handing in, from the assignment's own page
  await fetch(`${BASE}/__mock/reset`);
  await page.goto(`${BASE}/d2l/home/31001?simpl=${encodeURIComponent('/courses/31001/assignments/722')}`);
  await drawn();
  await page.goto(`${BASE}/d2l/home/31002?simpl=${encodeURIComponent('/courses/31002/assignments/722')}`);
  await drawn();
  const sourceA = await mainText();
  check(/Source Analysis/.test(sourceA) && /Submit|Hand in/i.test(sourceA), 'an assignment of another course, open to hand in');
  const submitBtn = await page.$('button:has-text("Submit assignment"), a:has-text("Submit assignment"), button:has-text("Hand in")');
  if (submitBtn) await submitBtn.click();
  await page.waitForTimeout(1200);
  const input = await page.$('input[type=file]');
  if (input) {
    await input.setInputFiles({ name: 'sources.pdf', mimeType: 'application/pdf', buffer: Buffer.from('%PDF-1.4 sources') });
    await page.waitForTimeout(800);
    const send = await page.$('button:has-text("Submit"):not(:has-text("assignment")), button:has-text("Hand in"), button:has-text("Turn in")');
    if (send) await send.click();
    await page.waitForTimeout(2500);
  }
  const subs = (await mockLog()).filter((e) => e.kind === 'submit');
  await shot('handed-in');
  check(!!input && subs.length === 1 && subs[0].folder === 722 && subs[0].files[0]?.name === 'sources.pdf', `a file handed in from the assignment’s page reaches the folder: ${JSON.stringify(subs.map((s) => ({ folder: s.folder, files: s.files.map((f) => f.name) })))}`);

  // a reply, from the discussion's page
  await fetch(`${BASE}/__mock/reset`);
  await page.goto(`${BASE}/d2l/home/31001?simpl=${encodeURIComponent('/courses/31001/discussion_topics/2000000902')}`);
  await drawn();
  const disc = await mainText();
  check(/Is a virus alive/.test(disc) && /Jordan Reyes/.test(disc) && /mule/.test(disc), 'a discussion: the prompt, the posts and the reply under them');
  await shot('discussion');

  // content: a module inside a module, and a quiz left to Brightspace
  await page.goto(`${BASE}/d2l/home/31001?simpl=${encodeURIComponent('/courses/31001/modules')}`);
  await drawn();
  const openAll = await page.$('button:has-text("Open all")');
  if (openAll) await openAll.click();
  await page.waitForTimeout(600);
  const content = await mainText();
  check(/Week 1: The Cell/.test(content) && /Optional Reading/i.test(content) && /Cell Biology Primer/.test(content) && !/Instructor notes/.test(content), 'Content: the modules, a module inside one as a heading, the hidden one not there');
  await shot('content');
  // a link of the interface's opened in a tab of its own (a middle click): the tab gets the Brightspace page for it
  const link = page.locator('#bcv-main a[href="/courses/31001/assignments/701"]').first();
  const before = await link.getAttribute('href').catch(() => null);
  const [tab] = await Promise.all([context.waitForEvent('page', { timeout: 8000 }).catch(() => null), link.click({ button: 'middle' }).catch(() => null)]);
  if (tab) await tab.waitForLoadState('domcontentloaded').catch(() => {});
  const tabAt = tab ? new URL(tab.url()) : null;
  await page.mouse.click(5, 300); // (the next press puts the link's own address back)
  const after = await link.getAttribute('href').catch(() => null);
  check(before === '/courses/31001/assignments/701' && tabAt?.pathname + tabAt?.search === '/d2l/home/31001?simpl=%2Fcourses%2F31001%2Fassignments%2F701' && after === before, `a link opened in a tab of its own goes to the Brightspace page for it (Brightspace has no page at the interface’s own address), and keeps its own address after: ${before} → ${tabAt ? tabAt.pathname + tabAt.search : 'no tab'} → ${after}`);
  if (tab) await tab.close().catch(() => {});
  await page.click('#bcv-main a[href="/courses/31001/files/1302"]');
  await page.waitForSelector('.bcv-viewer', { timeout: 10000 }).catch(() => {});
  await page.waitForTimeout(2500);
  const viewer = await page.evaluate(() => ({ title: document.querySelector('.bcv-viewer .bcv-sheet__title')?.textContent || '', frame: document.querySelector('.bcv-viewer__frame')?.getAttribute('src') || null, drawn: !!document.querySelector('.bcv-viewer canvas, .bcv-viewer .bcv-dv, .bcv-viewer [class*="pdf"]'), none: document.querySelector('.bcv-viewer__none')?.innerText || '' }));
  await shot('viewer');
  check(viewer.title === 'Cell Diagram' && (viewer.drawn || viewer.frame === '/d2l/le/content/31001/fullscreen/1302/View?skipHeader=True') && !viewer.none, `a file in Content opens in the viewer over the page — drawn from its bytes, or Brightspace’s own viewer: ${JSON.stringify(viewer)}`);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(500);
  await page.click('text=Chapter 4 Check');
  await page.waitForURL((u) => u.pathname === '/d2l/lms/quizzing/user/quiz_summary.d2l', { timeout: 15000 }).catch(() => {});
  await page.waitForTimeout(1500);
  const quiz = await state();
  check(quiz.path.startsWith('/d2l/lms/quizzing/user/quiz_summary.d2l?qi=802&ou=31001') && !quiz.on && (await page.isVisible('#d2l-quiz-start')), `a quiz opens on Brightspace’s own page, which the interface leaves as it is: ${JSON.stringify(quiz)}`);
  await shot('quiz-native');

  // the search box's commands: none for what Brightspace has no such thing for here
  await page.goto(`${BASE}/d2l/home`);
  await drawn();
  await page.keyboard.press('/');
  await page.waitForTimeout(400);
  await page.keyboard.type('/', { delay: 30 });
  await page.waitForTimeout(800);
  const cmds = await page.evaluate(() => [...document.querySelectorAll('.bcv-omni__t, .bcv-omni__tname')].map((e) => e.textContent.trim()).filter((t) => t.startsWith('/')));
  await page.keyboard.press('Escape');
  check(cmds.length > 5 && !cmds.some((t) => /^\/(inbox|groups|history)\b/.test(t)) && cmds.some((t) => /^\/grades\b/.test(t)), `the search box offers no /inbox, /groups or /history on Brightspace: ${cmds.slice(0, 12).join(' ')}`);

  await page.goto(`${BASE}/d2l/home#todo`);
  await drawn();
  await page.waitForTimeout(800);
  const todo = await mainText();
  check(/To Do/.test(todo) && /Essay Proposal/.test(todo), 'To Do lists what is due next');
  await page.goto(`${BASE}/d2l/home?simpl=${encodeURIComponent('/calendar')}`);
  await drawn();
  await page.waitForTimeout(800);
  const cal = await page.evaluate(() => ({ text: (document.querySelector('#bcv-main')?.innerText || ''), appt: !!document.querySelector('.bcv-cal__apptbtn') }));
  check(!cal.appt && /Calendars/.test(cal.text), 'the Calendar, without Canvas’s appointments');
  await shot('calendar');

  // a student new to Simpl: the setup opens over the homepage, and the address it opened at is cleaned at once — on
  // Brightspace the interface's ?bcv=setup rides inside ?simpl=, and a reload there would open the setup again
  await sw.evaluate(() => self.BCV.api.storage.local.remove('setup:done'));
  await page.goto(`${BASE}/d2l/home`);
  await page.waitForSelector('#bcv-setup', { state: 'attached', timeout: 20000 }).catch(() => {});
  await page.waitForFunction(() => !/bcv%3Dsetup|bcv=setup/.test(location.search), null, { timeout: 8000 }).catch(() => {});
  await page.waitForTimeout(1500);
  const setupAt = await state();
  check(!!(await page.$('#bcv-setup')) && setupAt.path === '/d2l/home', `a student new to it gets the setup, and the address is the homepage again at once (a reload lands on the page, not the setup): ${setupAt.path}`);
  await shot('setup');
  await sw.evaluate((v) => self.BCV.api.storage.local.set({ 'setup:done': true, 'whatsnew:seen': v }), manifest.version);

  // signed out: Brightspace sends the page to sign in, and the interface leaves that page alone
  await context.clearCookies();
  await page.goto(`${BASE}/d2l/home`);
  await page.waitForTimeout(1500);
  const out2 = await state();
  check(out2.path.startsWith('/d2l/login') && !out2.on, `signed out, the sign-in page is Brightspace’s own: ${JSON.stringify(out2)}`);
  check(!errors.length, `no page errors${errors.length ? `: ${errors.slice(0, 3).join(' | ')}` : ''}`);
} catch (e) {
  console.error('crashed:', e?.stack || e);
  failures.push('crash: ' + e.message);
} finally {
  await context?.close().catch(() => {});
  rmSync(userDataDir, { recursive: true, force: true });
  rmSync(extDir, { recursive: true, force: true });
}

// ---- the apps -----------------------------------------------------------------------------------------------------------
// The iPhone app and Simpl for Mac load the same scripts (app-bundle.mjs bundles them as ScriptBundle.swift does) with
// their own chrome on: the page is the engine of their native screens, and each screen asks it for its content
// (content/app/native-app.js). On Brightspace that content comes through the layer above, in the same shapes.
console.log('\nthe apps');
let appBrowser;
try {
  const { shellScript, nativeStub } = appBundle({ root, host: 'localhost' });
  appBrowser = await chromium.launch({ channel: 'chromium' });
  const actx = await appBrowser.newContext({ viewport: { width: 402, height: 874 }, isMobile: true, hasTouch: true });
  await actx.addCookies([{ name: 'd2lSessionVal', value: 'ok', url: BASE }]); // (signed in, as the app's sign-in leaves it)
  await actx.addInitScript(nativeStub);
  await actx.addInitScript(shellScript);
  const ap = await actx.newPage();
  const appErrors = [];
  ap.on('pageerror', (e) => appErrors.push(e.message));
  const told = (op) => ap.evaluate((o) => self.__nativeCalls.filter((c) => c.op === o), op);
  const nc = (name, args) => ap.evaluate(([n, a]) => self.BCVNative.call(n, a), [name, args || {}]);
  await fetch(`${BASE}/__mock/reset`);
  await ap.goto(`${BASE}/d2l/home`);
  await ap.waitForFunction(() => document.documentElement.classList.contains('bcv-settled') && !!self.BCVNative, null, { timeout: 25000 });
  const st = (await told('shell.state')).pop();
  check(st?.shell === true && st?.signedIn === true && st?.lms === 'd2l', `the page tells the app it is ready, signed in, and on Brightspace (the app’s words and its Inbox and Groups go by it): ${JSON.stringify(st && { shell: st.shell, signedIn: st.signedIn, lms: st.lms })}`);
  const snap = await nc('snapshot');
  check(snap.lms === 'd2l' && /Avery/.test(snap.me?.name || '') && snap.inboxUnread === 0, `the account for the app’s bar: ${JSON.stringify({ lms: snap.lms, me: snap.me?.name, notif: snap.notifUnread, inbox: snap.inboxUnread })}`);
  const wi = await nc('watchInfo');
  check(wi.lms === 'd2l' && wi.lp === '1.50' && wi.le === '1.82' && wi.me === '7001' && wi.courses?.some((c) => c.id === '31001' && c.name), `what the app’s new-activity check needs to read Brightspace itself — the platform, the API version, the student, the courses: ${JSON.stringify({ lms: wi.lms, lp: wi.lp, le: wi.le, me: wi.me, courses: wi.courses?.map((c) => `${c.id}:${c.name}`) })}`);
  const cs = await nc('courses');
  const bio = cs.rows?.find((r) => r.id === '31001');
  check(!cs.error && cs.rows.length >= 3 && cs.rows.every((r) => r.id && r.url) && !cs.rows.some((r) => r.id === '30980') && bio?.scoreText === '87.5%', `Courses: the term’s courses with their totals, none the student is closed out of: ${JSON.stringify(cs.rows?.map((r) => `${r.id} ${r.code} ${r.scoreText}`))}`);
  const gr = await nc('grades');
  check(!gr.error && gr.rows.some((r) => r.pct === 87.5) && gr.rows.find((r) => r.pct === 87.5)?.cats.length === 2, `Grades: each course’s total and its weighted categories: ${JSON.stringify(gr.rows?.map((r) => `${r.code} ${r.pctText} cats:${r.cats.length}`))}`);
  const td = await nc('todo', { group: 'date' });
  check(!td.error && td.sections.find((x) => x.title === 'Overdue')?.rows.some((r) => r.title === 'Lab Safety Form') && td.sections.flatMap((x) => x.rows).some((r) => r.title === 'Chapter 4 Check'), `To Do: the work still to do (what is handed in already, above, left out): ${JSON.stringify(td.sections?.map((x) => `${x.title}:${x.rows.map((r) => r.title).join('|')}`))}`);
  const nf = await nc('notifications');
  check(!nf.error && nf.total > 0 && nf.days.flatMap((d) => d.rows).some((r) => /Lab 2 moved to Thursday/.test(r.title)), `Notifications: the news and the grades given: ${JSON.stringify({ total: nf.total, titles: nf.days?.flatMap((d) => d.rows).map((r) => r.title).slice(0, 6) })}`);
  const now = new Date();
  const cal = await nc('calendar', { from: new Date(now - 14 * DAY).toISOString(), to: new Date(+now + 28 * DAY).toISOString() });
  check(!cal.error && cal.events.some((e) => /Lab 2: Osmosis/.test(e.title)), `Calendar: the work due, by day: ${JSON.stringify({ events: cal.events?.length, titles: cal.events?.map((e) => e.title).slice(0, 6) })}`);

  const home = await nc('home', { ctx: 'courses/31001' });
  const kinds = (home.sections || []).map((x) => x.kind);
  check(!home.error && home.kind === 'courses' && /BIO 110/.test(home.title) && ['assignments', 'modules', 'announcements'].every((k) => kinds.includes(k)) && home.open.some((r) => r.title === 'Chapter 4 Check') && home.announcements.length > 0, `a course’s home: its sections, the work open, the latest news: ${JSON.stringify({ title: home.title, sections: home.sections?.map((x) => `${x.kind}:${x.title || x.label}`), open: home.open?.map((r) => r.title) })}`);
  const as = await nc('assignments', { ctx: 'courses/31001' });
  const sec = (t) => as.sections?.find((x) => x.title === t)?.rows.map((r) => r.title) || [];
  check(!as.error && sec('Overdue').includes('Lab Safety Form') && sec('Upcoming').includes('Lab 2: Osmosis') && !as.sections.flatMap((x) => x.rows).some((r) => /hidden/i.test(r.title)), `Assignments: overdue, upcoming, the rest — a hidden folder left out: ${JSON.stringify(as.sections?.map((x) => `${x.title}:${x.rows.map((r) => r.title).join('|')}`))}`);
  const a = await nc('assignment', { course: '31001', id: '702' });
  check(!a.error && a.title === 'Lab 2: Osmosis' && a.canSubmit && a.types.includes('online_upload') && /data table/.test(a.html) && a.canvasUrl === '/courses/31001/assignments/702?bcv=native', `an assignment: its instructions, and handed in from the app: ${JSON.stringify({ title: a.title, canSubmit: a.canSubmit, types: a.types, why: a.why, open: a.canvasUrl })}`);
  const own = await nc('pageFor', { url: a.canvasUrl });
  check(own.url === `${BASE}/d2l/lms/dropbox/user/folder_submit_files.d2l?db=702&ou=31001&bcv=native`, `“Open in Brightspace” opens Brightspace’s own page for it, left as it is: ${own.url}`);
  const rs = await nc('resolveUrl', { url: '/courses/31001/pages/whatever' });
  check(rs.url?.startsWith(`${BASE}/d2l/`), `an address the app has no screen for leads to a Brightspace page (never a Canvas address, a bare 404 there): ${rs.url}`);

  const pdf = Buffer.from('%PDF-1.4 graph').toString('base64');
  const up = await nc('submit', { course: '31001', id: '702', type: 'online_upload', files: [{ name: 'graph.pdf', type: 'application/pdf', data: pdf }], comment: 'From my phone' });
  const tx = await nc('submit', { course: '31001', id: '703', type: 'online_text_entry', text: 'Osmosis was faster than I thought.' });
  const subs = (await mockLog()).filter((e) => e.kind === 'submit');
  const f1 = subs.find((e) => e.folder === 702), f2 = subs.find((e) => e.folder === 703);
  check(up.ok && tx.ok && f1?.files[0]?.name === 'graph.pdf' && f1.comment.Text === 'From my phone' && /Osmosis was faster/.test(f2?.files[0]?.text || ''), `handed in from the app — a file with a comment, and text — as Brightspace takes them, the token on each: ${JSON.stringify({ up, tx, files: subs.map((e) => `${e.folder}:${e.files.map((f) => f.name).join('|')}`) })}`);
  const a2 = await nc('assignment', { course: '31001', id: '702' });
  check(/^Submitted /.test(a2.submitted) && a2.submission.files.some((f) => f.name === 'graph.pdf' && f.url.startsWith(`${BASE}/d2l/api/le/1.82/31001/dropbox/folders/702/submissions/`)), `and the assignment says so at once, the file there to open: ${JSON.stringify({ submitted: a2.submitted, files: a2.submission?.files })}`);

  const an = await nc('announcements', { ctx: 'courses/31001' });
  const first = an.rows?.find((r) => /Lab 2 moved/.test(r.title));
  const t = first ? await nc('topic', { ctx: 'courses/31001', id: first.id }) : {};
  check(!an.error && an.rows.length === 2 && Number(first?.id) > 3e9 && t.announcement === true && /Thursday/.test(t.html), `Announcements: the course’s news, each opened as the announcement it is: ${JSON.stringify({ rows: an.rows?.map((r) => `${r.id}:${r.title}`), opened: t.title })}`);
  const ds = await nc('discussions', { ctx: 'courses/31001' });
  const wk3 = ds.sections?.flatMap((x) => x.rows).find((r) => r.title === 'Week 3 Discussion');
  const dt = wk3 ? await nc('topic', { ctx: 'courses/31001', id: wk3.id }) : {};
  check(!ds.error && wk3?.graded && dt.entries?.length > 0 && dt.canReply && !dt.announcement, `Discussions: a graded topic and its posts, threaded, open to a reply: ${JSON.stringify({ topics: ds.sections?.flatMap((x) => x.rows).map((r) => r.title), posts: dt.entries?.length, depth: dt.entries?.map((e) => e.depth) })}`);
  const rp = wk3 ? await nc('reply', { ctx: 'courses/31001', id: wk3.id, text: 'A virus needs a host to copy itself.' }) : {};
  const posted = (await mockLog()).filter((e) => e.kind === 'post').pop();
  check(rp.ok && posted?.topic === 902 && /needs a host/.test(posted.html), `a reply from the app is posted to the topic: ${JSON.stringify(posted)}`);
  const md = await nc('modules', { ctx: 'courses/31001' });
  const items = md.modules?.flatMap((m) => m.items) || [];
  const syl = items.find((i) => i.title === 'Syllabus');
  check(syl?.requirement === 'Mark done' && syl.markable === false, `a topic done by hand says so, with no Mark Done the app’s screen would offer and Brightspace refuse: ${JSON.stringify(syl && { requirement: syl.requirement, markable: syl.markable })}`);
  check(!md.error && md.modules.length > 0 && items.some((i) => i.header) && items.some((i) => i.indent > 0) && items.filter((i) => !i.header).every((i) => i.url), `Content: each module, a module inside one as a heading with its topics a step in, every topic somewhere to go: ${JSON.stringify(md.modules?.map((m) => `${m.name}[${m.items.map((i) => `${i.header ? '#' : ''}${'>'.repeat(i.indent)}${i.title}`).join(', ')}]`))}`);
  const qz = await nc('quizzes', { ctx: 'courses/31001' });
  check(!qz.error && qz.rows.some((r) => r.title === 'Midterm Quiz') && qz.rows.some((r) => r.title === 'Chapter 4 Check'), `Quizzes: the course’s quizzes: ${JSON.stringify(qz.rows?.map((r) => `${r.title} → ${r.url}`))}`);
  const pp = await nc('people', { ctx: 'courses/31001' });
  const names = pp.sections?.flatMap((x) => x.rows.map((r) => r.name)) || [];
  check(!pp.error && names.some((n) => /Avery Quinn/.test(n)) && pp.sections.length >= 2, `People: the classlist, by role: ${JSON.stringify(pp.sections?.map((x) => `${x.title}:${x.rows.length}`))}`);
  check(!appErrors.length, `no page errors in the app${appErrors.length ? `: ${appErrors.slice(0, 3).join(' | ')}` : ''}`);
} catch (e) {
  console.error('crashed:', e?.stack || e);
  failures.push('crash (the apps): ' + e.message);
} finally {
  await appBrowser?.close().catch(() => {});
  server.kill();
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);

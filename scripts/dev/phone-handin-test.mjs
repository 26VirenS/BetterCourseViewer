// Hand in from your phone (2.99.96; 2.99.135 the phone sends the files here), end to end: the mock Canvas with the
// extension, a relay of its own on another port (scripts/dev/mock-handin.mjs: the site's handler over its HandInStore —
// another origin, as simplcourses.com is to a school's Canvas), and the phone's page (site/public/handin/) played in a
// second browser from the QR code's link.
//
//   1. The sheet: its code holds the link and nothing else (drawn again here, module for module, from the link Copy link
//      gives), it says it is waiting; the phone opens the link: the assignment, sealed, opened there; the sheet says
//      "Phone connected". The phone sends a PDF of three parts and a text file: the PDF goes into the hand-in, the text
//      file is offered as a PDF (Convert, as Choose Files would) and goes in converted, each "from your phone"; what the
//      phone sent over the wire is sealed (not a byte of either file, nor a name), and the relay never saw the secret.
//      Nothing is handed in until Submit; Done closes the sheet (the relay forgets the files); Submit hands both in, and
//      the phone says "Handed in ✓".
//   2. An assignment that takes PDFs and no pictures: two photos from the phone's camera become the pages of one PDF
//      named after the assignment (a tall page and a wide one), in the dark.
//   3. Fifteen minutes without the phone (run short here): the code stops, Start Again makes a new one.
//   4. No relay to be reached: the old code — the assignment, opened in Canvas on the phone — and its receipt.
// Run: node scripts/dev/phone-handin-test.mjs
import { createRequire } from 'node:module';
import { spawn, execSync } from 'node:child_process';
import { cpSync, readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { afterMigration, launchExtension, glassCuts, keptOff } from './harness.mjs';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const qrcode = require(join(root, 'extension', 'lib', 'vendor', 'qrcode.js'));
const OUT = join(root, 'scripts/dev/out');
mkdirSync(OUT, { recursive: true });
const PORT = 8817, RELAY_PORT = 8818;
const BASE = `http://localhost:${PORT}`, RELAY = `http://localhost:${RELAY_PORT}`;
const IDLE = 20000; // (the code's 15 minutes without the phone, run short in this copy)
const extDir = join(tmpdir(), `bcv-ph-ext-${Date.now()}`);
cpSync(join(root, 'extension'), extDir, { recursive: true });
const manifest = JSON.parse(readFileSync(join(extDir, 'manifest.json'), 'utf8'));
for (const cs of manifest.content_scripts) if (!cs.matches.includes('https://lazy.simplcourses.invalid/*')) cs.matches.push(`${BASE}/*`); // (the on-demand modules keep their never-matching group: a page asks for them, as it does in a browser)
manifest.host_permissions.push(`${BASE}/*`, `${RELAY}/*`);
writeFileSync(join(extDir, 'manifest.json'), JSON.stringify(manifest, null, 2));
const fresh = keptOff(extDir); // (the assignment's types are changed between parts: each page asks Canvas afresh)
const handinJs = join(extDir, 'content', 'app', 'handin.js');
const shipped = 'const IDLE = 15 * 60e3;';
const src = readFileSync(handinJs, 'utf8');
writeFileSync(handinJs, src.replace(shipped, `const IDLE = ${IDLE};`));
const server = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-canvas.mjs'), String(PORT)], { stdio: 'ignore' });
const relay = spawn(process.execPath, [join(root, 'scripts', 'dev', 'mock-handin.mjs'), String(RELAY_PORT)], { stdio: 'ignore' });
for (let i = 0; i < 50; i++) { const up = await Promise.all([fetch(`${BASE}/__mock/last-api`).then(() => true, () => false), fetch(`${RELAY}/api/handin`).then((r) => r.ok, () => false)]); if (up.every(Boolean)) break; await new Promise((r) => setTimeout(r, 150)); }

const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };
check(fresh, 'the copy draws nothing from answers kept between page loads');
check(src.split(shipped).length === 2, `the code stops after 15 minutes without the phone (${shipped} in content/app/handin.js)`);
const kept = async () => (await (await fetch(`${RELAY}/__mock/handin/kept`)).json()).kept;
const relayHolds = async (text) => (await (await fetch(`${RELAY}/__mock/handin/grep`, { method: 'POST', body: text })).json()).found;
const canvas = (path, init) => fetch(`${BASE}${path}`, init).then((r) => r.text()).then((t) => JSON.parse(t.replace(/^while\(1\);/, '') || 'null')); // (Canvas guards its JSON as Canvas does)
const until = async (fn, ms = 15000, step = 150) => { const t = Date.now(); for (;;) { const v = await fn().catch(() => null); if (v) return v; if (Date.now() - t > ms) return null; await new Promise((r) => setTimeout(r, step)); } };
/** The code's modules, drawn again from `text` as the sheet draws them. */
const qrPath = (text) => { const q = qrcode(0, 'M'); q.addData(text); q.make(); const n = q.getModuleCount(); let d = ''; for (let r = 0; r < n; r++) for (let c = 0; c < n; c++) if (q.isDark(r, c)) d += `M${c + 4} ${r + 4}h1v1h-1z`; return { d, n }; };

const userDataDir = join(tmpdir(), `bcv-ph-profile-${Date.now()}`);
const { context, sw } = await launchExtension(chromium, userDataDir, extDir, { viewport: { width: 1400, height: 900 } });
const phoneBrowser = await chromium.launch({ headless: true });
try {
  await afterMigration(sw);
  await sw.evaluate(async (relayBase) => {
    await self.BCV.api.storage.local.set({ 'setup:offered': true, 'setup:done': true, 'welcome:search': true, 'setup:flow': 3, 'whatsnew:seen': self.BCV.api.runtime.getManifest().version, 'dev:handinBase': relayBase });
  }, RELAY);
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: BASE });
  const page = await context.newPage();
  page.on('pageerror', (e) => failures.push(`page error: ${e.message}`));
  const asg = '/courses/104/assignments/4002';
  const openHandIn = async () => {
    await page.goto(`${BASE}${asg}`, { waitUntil: 'commit' });
    await page.waitForSelector('.bcv-asg__pill.is-go', { timeout: 20000 }).catch(async (e) => { await page.screenshot({ path: join(OUT, '36-phone-handin-fail.png') }); console.log((await page.evaluate(() => document.body.innerText)).slice(0, 600)); throw e; });
    await page.click('.bcv-asg__pill.is-go');
    await page.waitForSelector('.bcv-sheet-ov--card .bcv-hand .bcv-sb__tabs', { timeout: 10000 });
    await page.waitForTimeout(450);
  };
  const sheetLink = async () => {
    await page.click('.bcv-phone-ov .bcv-phone__copy');
    return page.evaluate(() => navigator.clipboard.readText());
  };
  const statusLine = () => page.evaluate(() => document.querySelector('.bcv-phone-ov .bcv-phone__status')?.textContent || '');
  const handInFiles = () => page.evaluate(() => [...document.querySelectorAll('.bcv-sb__files .bcv-sb__file')].map((r) => ({ name: r.querySelector('.bcv-sb__fname')?.textContent, sub: r.querySelector('.bcv-sb__fsub')?.textContent })));
  const phoneRows = (ph) => ph.evaluate(() => [...document.querySelectorAll('#list .row')].map((r) => ({ name: r.querySelector('.name').textContent, state: r.dataset.state, meta: r.querySelector('.meta').textContent })));
  const newPhone = async (opts = {}) => {
    const ctx = await phoneBrowser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true, ...opts });
    const ph = await ctx.newPage();
    ph.on('pageerror', (e) => failures.push(`phone page error: ${e.message}`));
    const sent = [];
    ph.on('request', (rq) => { const b = rq.postDataBuffer(); if (b) sent.push({ url: rq.url(), body: b }); });
    return { ctx, ph, sent };
  };

  // ---- 1. the sheet, the phone, two files, Submit ------------------------------------------------------------
  console.log('phone hand-in: the phone sends, the computer hands in');
  await openHandIn();
  const go = await page.$('.bcv-sb__phonego');
  const tint = await page.evaluate(() => [document.querySelector('.bcv-sb__phonego'), document.querySelector('.bcv-sb__browse')].map((e) => e && getComputedStyle(e).color));
  check(!!go && (await go.textContent()) === 'Hand in from your phone' && !!tint[0] && tint[0] === tint[1], `the hand-in panel offers Hand in from your phone, a link in the course's colour as browse is: ${JSON.stringify(tint)}`);
  await go.click();
  await page.waitForSelector('.bcv-phone-ov .bcv-phone__qr path', { timeout: 10000 });
  await page.waitForTimeout(400);
  const sheet = await page.evaluate(() => {
    const ov = document.querySelector('.bcv-phone-ov');
    const svg = ov.querySelector('.bcv-phone__qr');
    const r = svg.getBoundingClientRect();
    return {
      title: ov.querySelector('.bcv-sheet__title').textContent,
      steps: [...ov.querySelectorAll('.bcv-phone__step')].map((e) => e.textContent),
      wait: ov.querySelector('.bcv-phone__status').textContent,
      d: svg.querySelector('path').getAttribute('d'), box: svg.getAttribute('viewBox'),
      w: Math.round(r.width), h: Math.round(r.height),
      fill: [svg.querySelector('rect').getAttribute('fill'), svg.querySelector('path').getAttribute('fill')],
      done: !!ov.querySelector('.bcv-phone__done:not([hidden])'),
    };
  });
  const link = await sheetLink();
  const secret = (new RegExp(`^${RELAY}/handin/#([A-Za-z0-9_-]{43})$`).exec(link) || [])[1];
  check(!!secret, `the code's link is the phone's page on the relay, its one secret after # (${link.length} characters): ${link.replace(/#.*/, '#…')}`);
  const want = qrPath(link);
  check(sheet.d === want.d && sheet.box === `0 0 ${want.n + 8} ${want.n + 8}` && want.n <= 37, `the code is that link and nothing else, with its quiet margin (${want.n} modules: version ${(want.n - 17) / 4})`);
  check(sheet.fill.join() === '#fff,#000' && sheet.w === 168 && sheet.h === 168, `drawn dark on white, 168 px square: ${JSON.stringify({ fill: sheet.fill, w: sheet.w, h: sheet.h })}`);
  check(sheet.title === 'Hand in from your phone' && sheet.steps.length === 3 && /comes? here|come here/.test(sheet.steps[2]) && sheet.wait === 'Waiting for your phone…' && !sheet.done, `the sheet says what to do and that it is waiting: ${JSON.stringify(sheet.steps)}`);
  let k = await kept();
  check(k.length === 1 && k[0].hand.state === 'open' && k[0].hand.offer === 1 && !k[0].hand.opened, 'the hand-in started on the relay: open, its sealed offer kept, the phone not there yet');
  check(!(await relayHolds('Week 2 Post Class')) && !(await relayHolds('F26-SPRK')), 'the relay holds the offer sealed: not the assignment\'s name, not its course\'s');
  const cut = await glassCuts(page);
  check(!cut.length, `the sheet's glass blurs the page behind it on Chrome, over the hand-in panel too (2.99.111): ${JSON.stringify(cut)}`);
  await page.screenshot({ path: join(OUT, '36-phone-handin.png') });

  const p1 = await newPhone();
  await p1.ph.goto(link);
  await p1.ph.waitForFunction(() => !document.getElementById('intro').classList.contains('skeleton'), null, { timeout: 15000 });
  const phoneSays = await p1.ph.evaluate(() => ({ title: document.getElementById('title').textContent, course: document.getElementById('course').textContent, takes: document.getElementById('takes').textContent, link: document.getElementById('link').textContent, accept: document.getElementById('files').getAttribute('accept') || '', photo: !document.getElementById('photoBtn').hidden, combine: !document.getElementById('combineRow').hidden }));
  check(phoneSays.title === 'Week 2 Post Class Assignment: GC articles' && phoneSays.course === 'F26-SPRK 010 103' && /\.pdf, \.docx, \.png, \.jpg/.test(phoneSays.takes) && phoneSays.link === 'Connected to your computer', `the phone opens the sealed offer: the assignment, its course, what it takes: ${JSON.stringify(phoneSays)}`);
  check(/\.pdf/.test(phoneSays.accept) && /\.jpg/.test(phoneSays.accept) && phoneSays.photo && !phoneSays.combine, `its picker takes those types (and what converts to them), Take Photo is there, photos go as they are: ${phoneSays.accept}`);
  const connected = await until(async () => (await statusLine()) === 'Phone connected' && page.evaluate(() => !!document.querySelector('.bcv-phone__row.is-connected .bcv-phone__qr.is-small')));
  check(!!connected, `the computer sees the phone: "${await statusLine()}", the code small in its corner`);
  await page.screenshot({ path: join(OUT, '36-phone-handin-connected.png') });

  const MARK = 'SEALED-ON-THE-WAY-0451';
  const pdfText = `%PDF-1.4\n% ${MARK}\n${'Lab notes, line after line.\n'.repeat(52000)}%%EOF\n`; // (three parts of 512 KiB; ASCII, so the mock's upload counts it byte for byte)
  const txt = `Notes from the phone. ${MARK}\nA second line.\n`;
  check(pdfText.length > 2 * 524288 && pdfText.length < 3 * 524288, `a file of three parts (${pdfText.length} bytes)`);
  await p1.ph.setInputFiles('#files', [{ name: 'Lab notes.pdf', mimeType: 'application/pdf', buffer: Buffer.from(pdfText) }, { name: 'notes.txt', mimeType: 'text/plain', buffer: Buffer.from(txt) }, { name: 'setup.exe', mimeType: 'application/octet-stream', buffer: Buffer.from('MZ') }]);
  const receiving = await until(async () => /^Receiving/.test(await statusLine()) && statusLine(), 15000, 60);
  check(!!receiving, `the computer shows it coming in: "${receiving}"`);
  // the text file: offered as a PDF, as Choose Files offers it (the question rises over the phone's sheet)
  const asked = await page.waitForSelector('.bcv-ask-ov .bcv-ask__ok', { timeout: 20000 }).then(() => true).catch(() => false);
  const question = asked ? await page.evaluate(() => document.querySelector('.bcv-ask-ov .bcv-sheet__title')?.textContent || '') : '';
  check(asked && /Convert to PDF/.test(question), `a type it takes only converted: asked, as Choose Files asks ("${question}")`);
  if (asked) await page.click('.bcv-ask-ov .bcv-ask__ok');
  const both = await until(async () => { const f = await handInFiles(); return f.length === 2 && f.every((x) => /ready to submit/.test(x.sub)) && f; }, 25000);
  const files1 = await handInFiles();
  check(!!both && files1.map((f) => f.name).join() === 'Lab notes.pdf,notes.pdf' && files1.every((f) => /from your phone/.test(f.sub)), `both in the hand-in as if chosen here, each "from your phone": ${JSON.stringify(files1)}`);
  const rows1 = await page.evaluate(() => [...document.querySelectorAll('.bcv-phone__item')].map((r) => ({ name: r.querySelector('.bcv-phone__iname').textContent, sub: r.querySelector('.bcv-phone__isub').textContent })));
  check(rows1.length === 2 && rows1.every((r) => r.sub === 'Added to your hand-in') && (await statusLine()) === '2 files in your hand-in', `the sheet lists each one, added: ${JSON.stringify(rows1)} · "${await statusLine()}"`);
  const pRows = await until(async () => { const r = await phoneRows(p1.ph); return r.filter((x) => x.state === 'added').length === 2 && r; });
  const pr = await phoneRows(p1.ph);
  check(!!pRows && pr.find((r) => r.name === 'setup.exe')?.state === 'refused' && pr.filter((r) => r.state === 'added').every((r) => /In your hand-in on your computer/.test(r.meta)), `the phone says each one is in the hand-in on the computer, and refused the type it cannot take: ${JSON.stringify(pr)}`);
  const sentBanner = await p1.ph.evaluate(() => ({ shown: !document.getElementById('sent').hidden, words: document.querySelector('#sent strong').textContent }));
  check(sentBanner.shown && sentBanner.words === 'Sent — hand it in on your computer', `and ends with: "${sentBanner.words}"`);
  await p1.ph.screenshot({ path: join(OUT, '36-phone-handin-phone.png') });
  const wire = Buffer.concat(p1.sent.map((x) => x.body));
  check(p1.sent.length >= 6 && !wire.includes(MARK) && !wire.includes('Lab notes') && !wire.includes('notes.txt') && !p1.sent.some((x) => /notes/i.test(x.url)), `what the phone sent over the wire is sealed: not a byte of either file, not a name (${p1.sent.length} requests, ${wire.length} bytes)`);
  const seen = (await (await fetch(`${RELAY}/__mock/handin/log`)).json()).seen;
  check(seen.length > 5 && !seen.some((l) => l.includes(secret)), `the relay never saw the secret: it stays after # (${seen.length} requests seen)`);
  k = (await kept())[0];
  check(k.hand.opened > 0 && k.files.length === 2 && k.files.every((f) => f.taken === 1 && f.meta === 0) && k.bytes === 0, 'the relay forgot each file as soon as the computer took it');
  const before = await canvas(`/api/v1/courses/104/assignments/4002/submissions/self`);
  check(!before.submitted_at && !(Number(before.attempt) > 0), 'nothing is handed in by itself: Canvas has no hand-in yet');
  await page.click('.bcv-phone-ov .bcv-phone__done');
  await page.waitForSelector('.bcv-phone-ov', { state: 'detached', timeout: 5000 }).catch(() => {});
  k = (await kept())[0];
  check(!(await page.$('.bcv-phone-ov')) && k.hand.state === 'closed' && k.hand.offer === 0, 'Done closes the sheet, and the relay is told: closed, its offer forgotten');
  const closedSaid = await until(() => p1.ph.evaluate(() => /Closed on your computer/.test(document.getElementById('sentSub').textContent)), 10000);
  check(!!closedSaid, 'the phone says it was closed on the computer: finish handing in there');
  await page.click('.bcv-sb__go');
  const landed = await page.waitForSelector('.bcv-sb__done', { timeout: 20000 }).then(() => true).catch(() => false);
  const sub1 = await canvas(`/api/v1/courses/104/assignments/4002/submissions/self`);
  const names1 = (sub1.attachments || []).map((x) => x.display_name);
  const gotPdf = sub1.attachments?.[0] ? Buffer.from((await canvas(`/__mock/uploaded/${sub1.attachments[0].id}`))?.latin1 || '', 'latin1') : Buffer.alloc(0);
  const gotConv = sub1.attachments?.[1] ? Buffer.from((await canvas(`/__mock/uploaded/${sub1.attachments[1].id}`))?.latin1 || '', 'latin1') : Buffer.alloc(0);
  check(landed && names1.join() === 'Lab notes.pdf,notes.pdf' && Buffer.compare(gotPdf, Buffer.from(pdfText)) === 0 && gotConv.subarray(0, 5).toString() === '%PDF-', `Submit hands both in: the PDF byte for byte as the phone had it, the text file converted (${names1.join(', ')}; ${gotPdf.length} and ${gotConv.length} bytes)`);
  const handed = await until(() => p1.ph.evaluate(() => !document.getElementById('handed').hidden && document.querySelector('#handed strong').textContent), 10000);
  check(handed === 'Handed in ✓' && (await p1.ph.evaluate(() => document.getElementById('sent').hidden && document.getElementById('files').disabled)), `and the phone says so: "${handed}"`);
  await p1.ph.screenshot({ path: join(OUT, '36-phone-handin-phone-done.png') });
  k = (await kept())[0];
  check(k.hand.state === 'handed-in' && k.bytes === 0 && k.hand.offer === 0, 'the relay keeps only that word of it, until its 30 minutes are up');
  await p1.ctx.close();

  // ---- 2. photos made one PDF, in the dark ---------------------------------------------------------------------
  console.log('phone hand-in: photos become one PDF');
  await canvas('/__mock/config', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ forgetSubmissions: true, ext: { 4002: ['pdf'] } }) });
  await openHandIn();
  await page.click('.bcv-sb__phonego');
  await page.waitForSelector('.bcv-phone-ov .bcv-phone__qr path', { timeout: 10000 });
  const combineSaid = await page.evaluate(() => document.querySelector('.bcv-phone-ov .bcv-phone__combine')?.textContent || '');
  check(/one PDF, “Week 2 Post Class Assignment - GC articles\.pdf”/.test(combineSaid), `an assignment taking PDFs and no pictures: the sheet says photos become one PDF, named after it: "${combineSaid}"`);
  const link2 = await sheetLink();
  check(link2 !== link && /#[A-Za-z0-9_-]{43}$/.test(link2), 'a new code: a new secret');
  const p2 = await newPhone({ colorScheme: 'dark' });
  await p2.ph.goto(link2);
  await p2.ph.waitForFunction(() => !document.getElementById('intro').classList.contains('skeleton'), null, { timeout: 15000 });
  const dark = await p2.ph.evaluate(() => ({ bg: getComputedStyle(document.body).backgroundColor, card: getComputedStyle(document.getElementById('intro')).backgroundColor, combine: !document.getElementById('combineRow').hidden && document.getElementById('combine').checked, sub: document.getElementById('combineSub').textContent, takes: document.getElementById('takes').textContent }));
  check(dark.bg === 'rgb(0, 0, 0)' && dark.card === 'rgb(28, 28, 30)', `the phone's page in the dark: ${dark.bg} under ${dark.card} cards`);
  check(dark.combine && /Photos become one PDF: Week 2 Post Class Assignment - GC articles\.pdf/.test(dark.sub) && dark.takes === 'This assignment takes .pdf files.', `Combine photos into one PDF, on: "${dark.sub}"`);
  // two photos from its camera (JPEGs made by the phone's own browser): a tall one and a wide one
  const shot = (w, h, hue) => p2.ph.evaluate(([w, h, hue]) => new Promise((res) => { const c = document.createElement('canvas'); c.width = w; c.height = h; const g = c.getContext('2d'); g.fillStyle = `hsl(${hue} 70% 55%)`; g.fillRect(0, 0, w, h); g.fillStyle = '#fff'; g.fillRect(w / 4, h / 4, w / 2, h / 2); c.toBlob((b) => { const r = new FileReader(); r.onload = () => res(r.result.split(',')[1]); r.readAsDataURL(b); }, 'image/jpeg', 0.9); }), [w, h, hue]);
  const tall = Buffer.from(await shot(900, 1200, 210), 'base64'), wide = Buffer.from(await shot(3000, 2000, 30), 'base64');
  await p2.ph.setInputFiles('#photo', { name: 'image.jpg', mimeType: 'image/jpeg', buffer: tall });
  const page1 = await until(async () => { const f = await handInFiles(); return f.length === 1 && f; }, 20000);
  await p2.ph.setInputFiles('#photo', { name: 'image.jpg', mimeType: 'image/jpeg', buffer: wide });
  const page2 = await until(async () => page.evaluate(() => [...document.querySelectorAll('.bcv-phone__isub')].map((e) => e.textContent).join('|')).then((t) => /Page 2 of/.test(t) && t), 20000);
  const files2 = await handInFiles();
  check(!!page1 && !!page2 && files2.length === 1 && files2[0].name === 'Week 2 Post Class Assignment - GC articles.pdf' && /from your phone/.test(files2[0].sub), `two photos, one PDF in the hand-in: ${JSON.stringify(files2)} · ${page2}`);
  const pr2 = await until(async () => { const r = await phoneRows(p2.ph); return r.length === 2 && r.every((x) => x.state === 'added') && r; });
  check(!!pr2 && pr2[0].meta === 'Page 1 of the PDF, in your hand-in' && /^Page 2 of/.test(pr2[1].meta) && /^Photo \d{4}-\d\d-\d\d at /.test(pr2[0].name), `the phone names each photo for when it was taken, and says which page it is: ${JSON.stringify(pr2)}`);
  await p2.ph.screenshot({ path: join(OUT, '36-phone-handin-phone-dark.png') });
  check(!(await relayHolds(tall.subarray(200, 260))) && !(await relayHolds(wide.subarray(200, 260))), 'the photos never sat on the relay as they are');
  await page.click('.bcv-phone-ov .bcv-sheet__close');
  await page.waitForTimeout(300);
  await page.click('.bcv-sb__go');
  const landed2 = await page.waitForSelector('.bcv-sb__done', { timeout: 20000 }).then(() => true).catch(() => false);
  const sub2 = await canvas(`/api/v1/courses/104/assignments/4002/submissions/self`);
  const pdf = sub2.attachments?.length === 1 ? ((await canvas(`/__mock/uploaded/${sub2.attachments[0].id}`))?.latin1 || '') : '';
  const boxes = [...pdf.matchAll(/MediaBox \[0 0 (\d+) (\d+)\]/g)].map((m) => `${m[1]}x${m[2]}`);
  const dims = [...pdf.matchAll(/\/Width (\d+) \/Height (\d+)/g)].map((m) => `${m[1]}x${m[2]}`);
  check(landed2 && pdf.startsWith('%PDF-1.4') && /\/Count 2/.test(pdf) && boxes.join() === '612x792,792x612' && (pdf.match(/\/DCTDecode/g) || []).length === 2, `handed in as one PDF of two pages, a tall one and a wide one turned to landscape: ${boxes.join(', ')}`);
  check(dims[0] === '900x1200' && dims[1] === '2200x1467', `each photo made upright and no larger than 2200 pixels on its long side, on the phone: ${dims.join(', ')}`);
  const handed2 = await until(() => p2.ph.evaluate(() => !document.getElementById('handed').hidden), 10000);
  check(!!handed2, 'and the phone says "Handed in ✓"');
  await p2.ctx.close();

  // ---- 3. fifteen minutes without the phone ------------------------------------------------------------------------
  console.log('phone hand-in: stopped without use');
  await canvas('/__mock/config', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify({ forgetSubmissions: true, ext: { 4002: ['pdf', 'docx', 'png', 'jpg'] } }) });
  await openHandIn();
  await page.click('.bcv-sb__phonego');
  await page.waitForSelector('.bcv-phone-ov .bcv-phone__qr path', { timeout: 10000 });
  const link3 = await sheetLink();
  const stopped = await page.waitForSelector('.bcv-phone-ov .bcv-phone__problem', { timeout: IDLE + 15000 }).then(() => true).catch(() => false);
  const stoppedSays = await page.evaluate(() => document.querySelector('.bcv-phone__problem .bcv-phone__lead')?.textContent || '');
  const id3 = (await kept()).at(-1);
  check(stopped && stoppedSays === 'Stopped after 15 minutes without use.' && id3.hand.state === 'closed', `nothing from the phone: "${stoppedSays}", and the relay told to forget it`);
  const p3 = await newPhone();
  await p3.ph.goto(link3);
  const expired = await until(() => p3.ph.evaluate(() => !document.getElementById('gone').hidden && document.getElementById('goneTitle').textContent), 10000);
  check(/Closed on your computer|expired/.test(expired || '') && (await p3.ph.evaluate(() => document.getElementById('files').disabled)), `its link, opened after, says so and sends nothing: "${expired}"`);
  await page.click('.bcv-phone__again');
  await page.waitForSelector('.bcv-phone-ov .bcv-phone__steps', { timeout: 10000 });
  const link4 = await sheetLink();
  check(link4 !== link3 && (await statusLine()) === 'Waiting for your phone…', 'Start Again: a new code, waiting');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(400);
  await p3.ctx.close();

  // ---- 4. no relay: the old code -----------------------------------------------------------------------------------
  console.log('phone hand-in: no relay, the old code');
  await sw.evaluate(async () => self.BCV.api.storage.local.set({ 'dev:handinBase': 'http://localhost:9' }));
  await openHandIn();
  await page.click('.bcv-sb__phonego');
  await page.waitForSelector('.bcv-phone-ov .bcv-phone__qr path', { timeout: 15000 });
  const old = await page.evaluate(() => {
    const ov = document.querySelector('.bcv-phone-ov');
    return { steps: [...ov.querySelectorAll('.bcv-phone__step')].map((e) => e.textContent), why: ov.querySelector('.bcv-phone__fallback')?.textContent || '', d: ov.querySelector('.bcv-phone__qr path').getAttribute('d') };
  });
  const oldWant = qrPath(`${BASE}${asg}?bcv=submit`);
  check(old.d === oldWant.d && /Canvas/.test(old.steps[1]) && /SC-H-NET/.test(old.why), `simplcourses.com out of reach: the old code, the assignment in Canvas on the phone, saying why: "${old.why}"`);
  const res = await fetch(`${BASE}/api/v1/courses/104/assignments/4002/submissions`, { method: 'POST', headers: { 'content-type': 'application/json', 'x-csrf-token': 'mock+csrf/token=' }, body: JSON.stringify({ submission: { submission_type: 'online_text_entry', body: '<p>Sent from the phone.</p>' } }) });
  const landed4 = await page.waitForSelector('.bcv-sb__done', { timeout: 20000 }).then(() => true).catch(() => false);
  const after = await page.evaluate(() => ({ sheet: !!document.querySelector('.bcv-phone-ov'), done: document.querySelector('.bcv-sb__done .bcv-sb__h2')?.textContent || '' }));
  check(res.ok && landed4 && !after.sheet && after.done === 'Submitted', `and a hand-in made in Canvas on the phone closes the sheet and shows the receipt: ${JSON.stringify(after)}`);
} catch (e) {
  failures.push(`crash: ${e.message}`);
  console.log(e);
} finally {
  await phoneBrowser.close().catch(() => {});
  await context.close().catch(() => {});
  server.kill();
  relay.kill();
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n${failures.map((x) => ` - ${x}`).join('\n')}` : '\nall passed');
process.exit(failures.length ? 1 : 0);

// Report a bug, the site's side (site/): the handler behind simplcourses.com/api/report on its own —
// what it takes, what it refuses and with which code, the shapes it hands a Discord, a Slack or any
// other webhook — and then the page itself (site/public/report/) in Chromium, served with that
// handler: what Simpl's button carried in shown and prefilled, the checks before sending, a
// screenshot added and one taken away, the report arriving at the webhook whole, and, with no
// webhook set, the GitHub issue offered instead without the email or the phone number.
// Run: node scripts/dev/report-test.mjs
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import { createServer } from 'node:http';
import { readFileSync, existsSync, mkdirSync } from 'node:fs';
import { join, extname } from 'node:path';
import { deflateSync } from 'node:zlib';
const require = createRequire(import.meta.url);
let chromium;
try { ({ chromium } = require('playwright')); } catch { const p = execSync('npm root -g').toString().trim(); ({ chromium } = createRequire(join(p, 'x.js'))('playwright')); }

const root = new URL('../..', import.meta.url).pathname;
const out = join(root, 'scripts', 'dev', 'out');
mkdirSync(out, { recursive: true });
const R = await import(join(root, 'site', 'src', 'report.js'));
const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };

/** A small PNG, made here (a flat colour, w × h). */
function png(w, h, [r, g, b] = [142, 68, 230]) {
  const crc = (buf) => { let c = ~0; for (const x of buf) { c ^= x; for (let k = 0; k < 8; k++) c = (c >>> 1) ^ (0xedb88320 & -(c & 1)); } return ~c >>> 0; };
  const chunk = (type, data) => { const t = Buffer.from(type); const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const c = Buffer.alloc(4); c.writeUInt32BE(crc(Buffer.concat([t, data]))); return Buffer.concat([len, t, data, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  const row = Buffer.alloc(1 + w * 3); for (let x = 0; x < w; x++) { row[1 + x * 3] = r; row[2 + x * 3] = g; row[3 + x * 3] = b; }
  const raw = Buffer.concat(Array.from({ length: h }, () => row));
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk('IHDR', ihdr), chunk('IDAT', deflateSync(raw)), chunk('IEND', Buffer.alloc(0))]);
}
const PNG = png(64, 40);
const form = (fields = {}, shots = []) => {
  const f = new FormData();
  for (const [k, v] of Object.entries({ category: 'Bug', text: 'The calendar skips a day.', ...fields })) if (v !== undefined) f.set(k, v);
  for (const s of shots) f.append('screenshot', s.blob, s.name);
  return f;
};
const post = (body, headers = {}) => new Request('https://simplcourses.com/api/report', { method: 'POST', body, headers });
const hookFetch = () => { const calls = []; const fn = async (url, init) => { calls.push({ url, init }); return new Response('ok', { status: 200 }); }; fn.calls = calls; return fn; };
const answer = async (res) => ({ status: res.status, ...(await res.json()) });

console.log('the handler');
check(JSON.stringify(R.cleanCodes('sc-c-404, SC-D-NET;junk SC-C-404 SC-TOOLONGCODE12 SC-B-503')) === '["SC-C-404","SC-D-NET","SC-B-503"]', 'codes: SC-… alone, upper-cased, each once');
check(/^SR-[0-9A-Z]+-[A-HJ-NP-Z2-9]{4}$/.test(R.reportId()) && R.reportId(0, () => 0) === 'SR-0-AAAA', `a report's id reads out easily: ${R.reportId()}`);
check((await answer(await R.handleReport(new Request('https://x/api/report'), {}))).code === 'SC-B-405', 'anything but POST is refused (SC-B-405)');
const noHook = await answer(await R.handleReport(post(form()), {}));
check(noHook.status === 503 && noHook.code === 'SC-B-503' && /not switched on/.test(noHook.message), `with no webhook set, it says reports are not switched on yet (SC-B-503): ${JSON.stringify(noHook)}`);
const refusals = [
  [{ category: 'Complaint' }, 'SC-B-KIND'], [{ text: '   ' }, 'SC-B-TEXT'], [{ email: 'nope' }, 'SC-B-EMAIL'], [{ phone: 'call me' }, 'SC-B-PHONE'],
];
const got = [];
for (const [f, code] of refusals) got.push(`${(await answer(await R.handleReport(post(form(f)), { REPORT_WEBHOOK: 'https://example.com/h' }, hookFetch()))).code}=${code}`);
check(got.every((g) => g.split('=')[0] === g.split('=')[1]), `a wrong kind, no words, an email or a phone that does not look right: each refused with its own code (${got.join(' ')})`);
const txt = await answer(await R.handleReport(post(form({}, [{ blob: new Blob(['hi'], { type: 'text/plain' }), name: 'a.txt' }])), { REPORT_WEBHOOK: 'https://example.com/h' }, hookFetch()));
const six = await answer(await R.handleReport(post(form({}, Array.from({ length: 6 }, (_, i) => ({ blob: new Blob([PNG], { type: 'image/png' }), name: `s${i}.png` })))), { REPORT_WEBHOOK: 'https://example.com/h' }, hookFetch()));
check(txt.code === 'SC-B-415' && six.code === 'SC-B-SHOTS', `a file that is not a picture (SC-B-415) and a sixth screenshot (SC-B-SHOTS) are refused`);
const bot = hookFetch();
const botAns = await answer(await R.handleReport(post(form({ website: 'http://spam' })), { REPORT_WEBHOOK: 'https://example.com/h' }, bot));
check(botAns.ok && bot.calls.length === 0, 'the field people never see, filled in: answered as sent, handed on nowhere');
// any other webhook: the report as JSON, the screenshots as data URLs
const gen = hookFetch();
const genAns = await answer(await R.handleReport(post(form({ category: 'Error', text: 'Grades page is blank.', codes: 'sc-g-500, SC-D-NET', email: 'sam@school.edu', phone: '+1 555 123 4567', version: '2.98.19', browser: 'Chrome 140', page: '/courses/101/grades' }, [{ blob: new Blob([PNG], { type: 'image/png' }), name: 'grades.png' }])), { REPORT_WEBHOOK: 'https://hooks.example.com/simpl' }, gen));
const genBody = gen.calls[0] ? JSON.parse(gen.calls[0].init.body) : {};
check(genAns.ok && /^SR-/.test(genAns.id) && gen.calls.length === 1 && gen.calls[0].url === 'https://hooks.example.com/simpl' && genBody.id === genAns.id && genBody.category === 'Error' && genBody.text === 'Grades page is blank.' && genBody.codes.join() === 'SC-G-500,SC-D-NET' && genBody.email === 'sam@school.edu' && genBody.phone === '+1 555 123 4567' && genBody.version === '2.98.19' && genBody.page === '/courses/101/grades' && genBody.screenshots.length === 1 && genBody.screenshots[0].dataUrl === `data:image/png;base64,${PNG.toString('base64')}` && genBody.summary.startsWith(`Error · ${genAns.id}`), `any other webhook gets the report whole, as JSON, the screenshot inside it: ${JSON.stringify({ ...genBody, screenshots: genBody.screenshots?.length })}`);
// Discord: the words as the message, mentions off, the screenshots as files; a long report whole in a text file
const dc = hookFetch();
const long = 'x'.repeat(2500);
const dcAns = await answer(await R.handleReport(post(form({ text: `@everyone ${long}` }, [{ blob: new Blob([PNG], { type: 'image/png' }), name: 'shot.png' }])), { REPORT_WEBHOOK: 'https://discord.com/api/webhooks/1/abc' }, dc));
const dcBody = dc.calls[0]?.init.body;
const payload = dcBody ? JSON.parse(dcBody.get('payload_json')) : {};
const f0 = dcBody?.get('files[0]'), f1 = dcBody?.get('files[1]');
check(dcAns.ok && payload.content.length <= 2000 && payload.content.startsWith(`Bug · ${dcAns.id}`) && /whole report is attached/.test(payload.content) && JSON.stringify(payload.allowed_mentions) === '{"parse":[]}' && f0?.name === `${dcAns.id}.txt` && (await f0.text()).includes(long) && f1?.name === 'shot.png' && f1.type === 'image/png' && Buffer.from(await f1.arrayBuffer()).equals(PNG), `a Discord webhook gets the message (mentions off, cut at 2000 characters), the whole report as a text file, and the screenshot as a file of its own`);
const sl = hookFetch();
await R.handleReport(post(form({}, [{ blob: new Blob([PNG], { type: 'image/png' }), name: 'shot.png' }])), { REPORT_WEBHOOK: 'https://hooks.slack.com/services/T/B/C' }, sl);
check(sl.calls.length === 1 && /The calendar skips a day\./.test(JSON.parse(sl.calls[0].init.body).text) && /not carried to Slack/.test(JSON.parse(sl.calls[0].init.body).text), 'a Slack webhook gets the words, and is told the screenshots stay behind');
const bad = await answer(await R.handleReport(post(form()), { REPORT_WEBHOOK: 'https://hooks.example.com/down' }, async () => new Response('no', { status: 500 })));
check(bad.status === 502 && bad.code === 'SC-B-502', 'a webhook that refuses it: SC-B-502, try again');
let limited = null;
for (let i = 0; i < 7; i++) limited = await R.handleReport(post(form(), { 'cf-connecting-ip': '203.0.113.9' }), { REPORT_WEBHOOK: 'https://hooks.example.com/h' }, hookFetch());
check(limited.status === 429 && (await limited.json()).code === 'SC-B-429', 'a seventh report from one address inside ten minutes: SC-B-429');

// ---- the page, served with the handler, a webhook that keeps what it is sent ------------------------
console.log('the page');
const PORT = 8873, HOOK = 8874;
const BASE = `http://localhost:${PORT}`;
const env = { REPORT_WEBHOOK: `http://localhost:${HOOK}/hook` };
const received = [];
const hook = createServer((req, res) => { const chunks = []; req.on('data', (c) => chunks.push(c)); req.on('end', () => { received.push(JSON.parse(Buffer.concat(chunks).toString())); res.end('ok'); }); });
const TYPES = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css', '.png': 'image/png' };
const site = createServer(async (req, res) => {
  const url = new URL(req.url, BASE);
  if (url.pathname.startsWith('/api/')) {
    const chunks = [];
    for await (const c of req) chunks.push(c);
    const r = await R.handleReport(new Request(url, { method: req.method, headers: req.headers, body: req.method === 'GET' || req.method === 'HEAD' ? undefined : Buffer.concat(chunks) }), env);
    res.writeHead(r.status, Object.fromEntries(r.headers));
    res.end(Buffer.from(await r.arrayBuffer()));
    return;
  }
  const p = join(root, 'site', 'public', url.pathname.endsWith('/') ? `${url.pathname}index.html` : url.pathname);
  if (!p.startsWith(join(root, 'site', 'public')) || !existsSync(p)) { res.writeHead(404); res.end('not found'); return; }
  res.writeHead(200, { 'content-type': TYPES[extname(p)] || 'application/octet-stream' });
  res.end(readFileSync(p));
});
await new Promise((r) => hook.listen(HOOK, r));
await new Promise((r) => site.listen(PORT, r));
const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage({ viewport: { width: 1200, height: 1000 } });
  const errors = [];
  page.on('pageerror', (e) => errors.push(e.message));
  const q = new URLSearchParams({ c: 'error', codes: 'sc-c-404,SC-D-NET,bogus', v: '2.98.19', b: 'Chrome 140', p: '/courses/101/assignments/1009' });
  await page.goto(`${BASE}/report/?${q}`);
  await page.waitForSelector('#report');
  const pre = await page.evaluate(() => ({ title: document.title, h1: document.querySelector('h1').textContent.trim(), kinds: [...document.querySelectorAll('input[name="category"]')].map((i) => i.value).join(' | '), on: document.querySelector('input[name="category"]:checked')?.value, codes: document.getElementById('codes').value, sent: !document.getElementById('sent').hidden, meta: [...document.querySelectorAll('#meta dt, #meta dd')].map((e) => e.textContent).join(' | '), email: !!document.getElementById('email'), phone: !!document.getElementById('phone'), drop: !!document.getElementById('drop') }));
  check(pre.title === 'Report a bug — Simpl Courses' && pre.h1 === 'Report a bug' && pre.kinds === 'Bug | Error | Crash | Reason to disable | Missing feature | Feature request' && pre.on === 'Error' && pre.codes === 'SC-C-404, SC-D-NET' && pre.sent && pre.meta === 'Simpl version | 2.98.19 | Browser | Chrome 140 | Canvas page | /courses/101/assignments/1009' && pre.email && pre.phone && pre.drop, `the page: the six kinds, the one Simpl's button named chosen, the codes it saw filled in (the bogus one left out), what else is sent shown before anything is: ${JSON.stringify(pre)}`);
  await page.screenshot({ path: join(out, 'report-page.png'), fullPage: true });
  // the checks before sending: no words
  await page.click('#send');
  const noWords = await page.evaluate(() => ({ err: document.getElementById('textErr').textContent, status: document.getElementById('status').textContent, focus: document.activeElement?.id }));
  check(noWords.err === 'Say what happened.' && /needs a look/.test(noWords.status) && noWords.focus === 'text' && received.length === 0, `nothing is sent without words: it says so and puts the cursor there (${JSON.stringify(noWords)})`);
  await page.fill('#text', 'The assignment page shows a blank box where the rubric should be.');
  await page.fill('#email', 'nope');
  await page.click('#send');
  check((await page.textContent('#emailErr')) === 'That does not look like an email address.' && received.length === 0, 'an email that does not look like one is caught before sending');
  await page.fill('#email', 'sam@school.edu');
  await page.fill('#phone', '+1 555 123 4567');
  // screenshots: two added, one taken away
  await page.setInputFiles('#files', [{ name: 'first.png', mimeType: 'image/png', buffer: png(320, 200) }, { name: 'second.png', mimeType: 'image/png', buffer: png(120, 90, [52, 199, 89]) }]);
  await page.waitForFunction(() => document.querySelectorAll('#thumbs li').length === 2);
  await page.click('#thumbs li:nth-child(2) button');
  await page.waitForFunction(() => document.querySelectorAll('#thumbs li').length === 1);
  check((await page.getAttribute('#thumbs li img', 'alt')) === 'Screenshot 1', 'screenshots: two added show as thumbnails, and an X takes one away');
  await page.screenshot({ path: join(out, 'report-filled.png'), fullPage: true });
  await page.click('#send');
  await page.waitForSelector('.done', { timeout: 8000 });
  const doneText = (await page.textContent('.done')).replace(/\s+/g, ' ').trim();
  const r0 = received[0] || {};
  check(received.length === 1 && r0.category === 'Error' && r0.text === 'The assignment page shows a blank box where the rubric should be.' && r0.codes.join() === 'SC-C-404,SC-D-NET' && r0.email === 'sam@school.edu' && r0.phone === '+1 555 123 4567' && r0.version === '2.98.19' && r0.browser === 'Chrome 140' && r0.page === '/courses/101/assignments/1009' && r0.screenshots.length === 1 && r0.screenshots[0].name === 'first.png' && r0.screenshots[0].dataUrl.startsWith('data:image/png;base64,') && doneText.includes(r0.id) && /on its way/.test(doneText), `sent: the webhook has it whole — the kind, the words, the codes, the email and phone, where it came from, the one screenshot left — and the page says so with its id (${doneText})`);
  await page.screenshot({ path: join(out, 'report-sent.png') });
  // no webhook set: the GitHub issue, without the email or the phone
  env.REPORT_WEBHOOK = '';
  await page.goto(`${BASE}/report/?c=Feature%20request`);
  await page.fill('#text', 'A dark mode for the calendar\nplease');
  await page.fill('#email', 'sam@school.edu');
  await page.fill('#phone', '+1 555 123 4567');
  await page.click('#send');
  await page.waitForSelector('#fallback:not([hidden])', { timeout: 8000 });
  const fb = await page.evaluate(() => ({ status: document.getElementById('status').textContent, code: document.querySelector('#status code')?.textContent, href: document.querySelector('#fallback a')?.href, words: document.getElementById('fallback').textContent }));
  const issue = new URL(fb.href || 'https://x/');
  check(/not switched on/.test(fb.status) && fb.code === 'SC-B-503' && issue.origin + issue.pathname === 'https://github.com/26VirenS/BetterCourseViewer/issues/new' && issue.searchParams.get('title') === 'Feature request: A dark mode for the calendar' && issue.searchParams.get('body').includes('please') && !issue.searchParams.get('body').includes('sam@school.edu') && !issue.searchParams.get('body').includes('555') && /left out/.test(fb.words), `with no webhook set it says so, with its code, and offers a GitHub issue carrying the words — never the email or the phone number (${fb.href})`);
  await page.screenshot({ path: join(out, 'report-fallback.png'), fullPage: true });
  // a phone's width: nothing wider than the screen
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(`${BASE}/report/`);
  const over = await page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
  check(over <= 0, `at a phone's width nothing runs off the side (${over}px)`);
  await page.screenshot({ path: join(out, 'report-phone.png'), fullPage: true });
  check(errors.length === 0, `no script errors on the page${errors.length ? `: ${errors.join(' | ')}` : ''}`);
} finally {
  await browser.close();
  site.close();
  hook.close();
}
console.log(failures.length ? `\n${failures.length} check(s) failed:\n - ${failures.join('\n - ')}` : '\nAll checks passed.');
process.exit(failures.length ? 1 : 0);

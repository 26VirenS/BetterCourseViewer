// (2.99.135) Hand in from your phone's sealed relay, without a browser: the site's handler (site/src/handin.js) over its
// HandInStore (site/src/handin-store.js) with a stand-in for the HANDIN namespace whose storage is real SQL (node:sqlite,
// scripts/dev/mock-handin.mjs), driven with the extension's own sealing (content/app/handin.js, its protocol half) — the
// tokens and who may use which, the limits (a part's size, 10 files, 50 MB, starts from one address), the sealing (the
// relay never holds a byte of plain text, nor the key; a part moved to another place does not open), the parts in order,
// the computer taking a file, closing, "handed-in", and forgetting (30 minutes after the last thing done, by its alarm
// and by the clock); every refusal's code, and each one in docs/ERROR-CODES.md; the phone's page (its CSP hash, the
// one address it asks); and the photos' PDF the computer makes.
// Run: node scripts/dev/handin-test.mjs
import { readFileSync } from 'node:fs';
import { createHash, randomBytes } from 'node:crypto';
import { join } from 'node:path';
import vm from 'node:vm';

const root = new URL('../..', import.meta.url).pathname;
const H = await import(join(root, 'site', 'src', 'handin.js'));
const { memoryNamespace, keptBlobs } = await import(join(root, 'scripts', 'dev', 'mock-handin.mjs'));
await import(join(root, 'extension', 'content', 'app', 'handin.js'));
const C = globalThis.BCV.handinCore;
const failures = [];
const check = (ok, label) => { console.log(`  ${ok ? '✓' : '✗'} ${label}`); if (!ok) failures.push(label); };
const enc = new TextEncoder(), dec = new TextDecoder();
const junk = (n) => new Uint8Array(randomBytes(n)); // (bytes that stand for sealed ones: the relay cannot tell)

const HANDIN = memoryNamespace();
const env = { HANDIN };
let now = Date.UTC(2026, 10, 1, 18, 0);
let ip = 0;
const codes = new Set(), uncoded = [];
/** One request to the relay, as the Worker gets it. Every refusal's code is noted. */
async function call(method, path, { token, body, headers = {}, e = env, from = null } = {}) {
  const h = { 'cf-connecting-ip': from || `10.2.0.${++ip}`, ...headers };
  if (token) h.authorization = `Bearer ${token}`;
  if (body && !(body instanceof Uint8Array) && typeof body === 'object') { body = JSON.stringify(body); h['content-type'] = 'application/json'; }
  const r = await H.handleHandIn(new Request(`https://simplcourses.com/api/handin${path}`, { method, body, headers: h }), e, now);
  const type = r.headers.get('content-type') || '';
  const out = { status: r.status, headers: r.headers, json: null, bytes: null };
  if (/json/.test(type)) out.json = await r.json(); else out.bytes = new Uint8Array(await r.arrayBuffer());
  if (r.status >= 400) { if (/^SC-H-/.test(out.json?.code || '')) codes.add(out.json.code); else uncoded.push(`${method} ${path} ${r.status}`); }
  return out;
}
const objOf = (id) => HANDIN.objects.get(id);
const db = (id) => objOf(id).storage.db;

console.log('handin: the store');
let r = await call('GET', '');
check(r.status === 200 && r.json.store === true, 'the store is there (/api/handin)');
r = await call('GET', '', { e: {} });
check(r.json.store === false, 'and says so when it is not');
r = await call('PUT', `/${C.b64u(C.rand(16))}`, { e: {}, token: C.b64u(C.rand(32)), body: {} });
check(r.status === 503 && r.json.code === 'SC-H-503', 'no store: refused, SC-H-503 (the extension then shows the old code)');
r = await call('OPTIONS', `/${C.b64u(C.rand(16))}/file/${C.b64u(C.rand(8))}/0`);
check(r.status === 204 && /PUT/.test(r.headers.get('access-control-allow-methods')) && /x-chunks/.test(r.headers.get('access-control-allow-headers')) && r.headers.get('access-control-allow-origin') === '*', 'a preflight answered: open to any origin, no cookie (the tokens are the only proofs)');

console.log('handin: started by the computer');
const secret = C.b64u(C.rand(32)), read = C.b64u(C.rand(32));
const k = await C.derive(secret);
const k2 = await C.derive(secret);
check(/^[A-Za-z0-9_-]{22}$/.test(k.id) && /^[A-Za-z0-9_-]{43}$/.test(k.send) && k.id === k2.id && k.send === k2.send, 'one secret gives the id and the phone\'s token, the same each time (HKDF): the QR code holds 43 characters of secret');
const other = await C.derive(C.b64u(C.rand(32)));
check(other.id !== k.id && other.send !== k.send, 'another secret, another hand-in');
const offer = C.offerOf({ title: 'Lab 3: Forces', course: 'Physics 8', color: '#0a84ff', allowed: ['pdf'], convertible: ['docx'], pdfName: 'Lab 3 - Forces.pdf' });
const offerSealed = await C.seal(k.key, C.aad(k.id, 'offer'), enc.encode(JSON.stringify(offer)));
const start = { send: k.send, offer: C.b64u(offerSealed) };
const P = `/${k.id}`;
r = await call('PUT', P, { body: start });
check(r.status === 401 && r.json.code === 'SC-H-401', 'no token: refused, SC-H-401');
r = await call('PUT', '/short', { token: read, body: start });
check(r.status === 404, 'a malformed id: nothing there');
r = await call('PUT', P, { token: read, body: { send: k.send, offer: 'AAAA' } });
check(r.status === 400 && r.json.code === 'SC-H-400', 'an offer too short to be sealed: refused, SC-H-400');
r = await call('PUT', P, { token: read, body: { send: read, offer: start.offer } });
check(r.status === 400, 'the phone\'s token the same as the computer\'s: refused');
r = await call('PUT', P, { token: read, body: { send: k.send, offer: C.b64u(C.rand(H.LIMITS.offer + 1)) } });
check(r.status === 400, 'an offer over 16 KB: refused');
r = await call('PUT', P, { token: read, body: start });
check(r.status === 201 && r.json.ok && r.json.expires === now + 30 * 60e3, 'started: kept for 30 minutes from now');
r = await call('PUT', P, { token: read, body: start });
check(r.status === 200, 'the same start again (its answer lost): taken');
r = await call('PUT', P, { token: C.b64u(C.rand(32)), body: start });
check(r.status === 403 && r.json.code === 'SC-H-403', 'another token cannot take the id over (SC-H-403)');
const hand = db(k.id).prepare('SELECT * FROM hand').get();
check(/^[0-9a-f]{64}$/.test(hand.read) && /^[0-9a-f]{64}$/.test(hand.send) && hand.read !== read && hand.send !== k.send && hand.read !== hand.send, 'only the two tokens\' hashes are kept');
check(objOf(k.id).storage.alarm === now + 30 * 60e3, 'and an alarm set to forget it');

console.log('handin: the phone opens the offer');
r = await call('GET', `${P}/offer`, { token: read });
check(r.status === 403, 'the computer\'s token cannot read as the phone');
r = await call('GET', P, { token: k.send });
check(r.status === 403, 'nor the phone\'s look as the computer');
r = await call('GET', P, { token: read });
check(r.status === 200 && r.json.state === 'open' && r.json.opened === false && r.json.files.length === 0, 'the computer\'s look: open, the phone not here yet');
r = await call('GET', `${P}/offer`, { token: k.send });
const got = JSON.parse(dec.decode(await C.open(k.key, C.aad(k.id, 'offer'), r.bytes)));
check(r.status === 200 && got.title === 'Lab 3: Forces' && got.photos === 'pdf' && got.pdfName === 'Lab 3 - Forces.pdf', 'the phone opens the sealed offer with the key from the secret: the assignment, and photos made one PDF');
let bad = false;
try { await C.open(other.key, C.aad(k.id, 'offer'), r.bytes); } catch { bad = true; }
check(bad, 'another key cannot open it');
r = await call('GET', P, { token: read });
check(r.json.opened === true, 'and the computer sees the phone has it ("Phone connected")');

console.log('handin: a file, sent in sealed parts');
const CH = H.LIMITS.chunk;
check(CH === C.LIMITS.chunk && H.LIMITS.files === C.LIMITS.files && H.LIMITS.total === C.LIMITS.total, `the extension and the relay agree on the limits (${CH} bytes a part, ${H.LIMITS.files} files, ${H.LIMITS.total} bytes)`);
const marker = 'PLAIN-TEXT-THAT-MUST-NEVER-BE-KEPT';
const plain = new Uint8Array(CH * 2 + 1000);
for (let i = 0; i < plain.length; i++) plain[i] = (i * 31) & 255;
plain.set(enc.encode(marker), 10);
plain.set(enc.encode(marker), CH + 10);
const f = C.b64u(C.rand(8));
const meta = { name: 'Lab report.pdf', type: 'application/pdf', size: plain.length, chunks: 3 };
const metaSealed = await C.seal(k.key, C.aad(k.id, 'meta', f), enc.encode(JSON.stringify(meta)));
const F = `${P}/file/${f}`;
r = await call('PUT', F, { token: k.send, body: metaSealed });
check(r.status === 413, 'a file not saying how many parts it has: refused');
r = await call('PUT', F, { token: k.send, body: metaSealed, headers: { 'x-chunks': String(H.LIMITS.chunks + 1) } });
check(r.status === 413 && r.json.code === 'SC-H-413', `a file of more parts than 50 MB makes (${H.LIMITS.chunks + 1}): refused, SC-H-413`);
r = await call('PUT', F, { token: k.send, body: C.rand(H.LIMITS.meta + 29), headers: { 'x-chunks': '3' } });
check(r.status === 413, 'a description over 4 KB: refused');
r = await call('PUT', F, { token: read, body: metaSealed, headers: { 'x-chunks': '3' } });
check(r.status === 403, 'the computer\'s token cannot send as the phone');
r = await call('PUT', `${F}/0`, { token: k.send, body: C.rand(100) });
check(r.status === 404, 'a part before its file is described: nothing there');
r = await call('PUT', F, { token: k.send, body: metaSealed, headers: { 'x-chunks': '3' } });
check(r.status === 201 && r.json.seq === 1, 'described: the first file');
r = await call('PUT', F, { token: k.send, body: metaSealed, headers: { 'x-chunks': '3' } });
check(r.status === 200, 'the same description again (its answer lost): taken');
r = await call('PUT', F, { token: k.send, body: await C.seal(k.key, C.aad(k.id, 'meta', f), enc.encode('{}')), headers: { 'x-chunks': '3' } });
check(r.status === 409 && r.json.code === 'SC-H-409', 'another description for the same file: refused, SC-H-409');
const sealedParts = [];
for (let i = 0; i < 3; i++) sealedParts.push(await C.seal(k.key, C.aad(k.id, 'chunk', f, String(i)), plain.subarray(i * CH, Math.min(plain.length, (i + 1) * CH))));
r = await call('PUT', `${F}/1`, { token: k.send, body: sealedParts[1] });
check(r.status === 409 && /order/i.test(r.json.message), 'part 1 before part 0: refused, out of order');
r = await call('PUT', `${F}/0`, { token: k.send, body: await C.seal(k.key, C.aad(k.id, 'chunk', f, '0'), plain.subarray(0, 1000)) });
check(r.status === 400, 'a part short of 512 KiB that is not the last: refused');
r = await call('PUT', `${F}/0`, { token: k.send, body: junk(CH + 29) });
check(r.status === 413, 'a part over 512 KiB: refused');
r = await call('PUT', `${F}/0`, { token: k.send, body: sealedParts[0] });
check(r.status === 200 && r.json.got === 1 && r.json.done === false, 'part 0 kept');
r = await call('PUT', `${F}/0`, { token: k.send, body: sealedParts[0] });
check(r.status === 200 && r.json.got === 1, 'part 0 again (its answer lost): kept once, in its place');
r = await call('GET', P, { token: read });
check(r.json.files.length === 1 && r.json.files[0].got === 1 && r.json.files[0].chunks === 3 && r.json.files[0].taken === false, 'the computer sees it coming: 1 of 3 parts ("Receiving… 33%")');
r = await call('PUT', `${F}/1`, { token: k.send, body: sealedParts[1] });
r = await call('PUT', `${F}/2`, { token: k.send, body: sealedParts[2] });
check(r.status === 200 && r.json.got === 3 && r.json.done === true, 'parts 1 and 2: the file is whole');
r = await call('PUT', `${F}/2`, { token: k.send, body: sealedParts[2] });
check(r.status === 200 && r.json.done === true, 'its last part again: still whole, nothing doubled');
r = await call('PUT', `${F}/3`, { token: k.send, body: sealedParts[2] });
check(r.status === 409, 'a part past its last: refused');
const blobs = keptBlobs(HANDIN);
check(blobs.length >= 5 && !blobs.some((b) => Buffer.from(b).includes(marker)) && !blobs.some((b) => Buffer.from(b).includes('Lab report')) && !blobs.some((b) => Buffer.from(b).includes('Physics')), 'nothing kept holds a byte of plain text: not the file, not its name, not the assignment');
const everything = db(k.id).prepare('SELECT * FROM hand').all().concat(db(k.id).prepare('SELECT * FROM file').all()).map((x) => JSON.stringify(Object.values(x).map((v) => (v instanceof Uint8Array ? Buffer.from(v).toString('base64') : v)))).join();
check(!everything.includes(secret) && !everything.includes(k.send) && !everything.includes(read) && !blobs.some((b) => Buffer.from(b).includes(Buffer.from(C.unb64u(secret)))), 'nor the secret, nor either token');

console.log('handin: the computer takes it');
r = await call('GET', `${F}/0`, { token: k.send });
check(r.status === 403, 'the phone\'s token cannot read the parts back');
r = await call('GET', F, { token: read });
const metaBack = JSON.parse(dec.decode(await C.open(k.key, C.aad(k.id, 'meta', f), r.bytes)));
check(metaBack.name === 'Lab report.pdf' && metaBack.size === plain.length, 'the computer opens the description');
const back = [];
for (let i = 0; i < 3; i++) { r = await call('GET', `${F}/${i}`, { token: read }); back.push(await C.open(k.key, C.aad(k.id, 'chunk', f, String(i)), r.bytes)); }
const whole = Buffer.concat(back);
check(whole.length === plain.length && Buffer.compare(whole, Buffer.from(plain)) === 0, 'and the parts, in order: the file, byte for byte');
r = await call('GET', `${F}/1`, { token: read });
bad = false;
try { await C.open(k.key, C.aad(k.id, 'chunk', f, '0'), r.bytes); } catch { bad = true; }
let bad2 = false;
try { await C.open(k.key, C.aad(k.id, 'chunk', C.b64u(C.rand(8)), '1'), r.bytes); } catch { bad2 = true; }
let bad3 = false;
try { await C.open(k.key, C.aad(other.id, 'chunk', f, '1'), r.bytes); } catch { bad3 = true; }
check(bad && bad2 && bad3, 'a part does not open in another place: another index, another file, another hand-in');
r = await call('DELETE', F, { token: k.send });
check(r.status === 403, 'the phone cannot take a file');
r = await call('DELETE', F, { token: read });
const fileRow = db(k.id).prepare('SELECT * FROM file WHERE f = ?').get(f);
check(r.status === 200 && fileRow.taken === 1 && fileRow.meta === null && db(k.id).prepare('SELECT COUNT(*) AS n FROM chunk').get().n === 0, 'taken: its parts and its description forgotten at once');
r = await call('GET', `${P}/status`, { token: k.send });
check(r.status === 200 && r.json.state === 'open' && r.json.files[0].f === f && r.json.files[0].done && r.json.files[0].taken, 'the phone sees it is on the computer ("In your hand-in")');
r = await call('PUT', `${F}/0`, { token: k.send, body: sealedParts[0] });
check(r.status === 200 && r.json.done === true && db(k.id).prepare('SELECT COUNT(*) AS n FROM chunk').get().n === 0, 'a part of it sent again after: nothing kept');

console.log('handin: limits');
const sizes = [];
for (let n = 2; n <= 10; n++) { r = await call('PUT', `${P}/file/${C.b64u(C.rand(8))}`, { token: k.send, body: C.rand(80), headers: { 'x-chunks': '1' } }); sizes.push(r.status); }
check(sizes.every((s) => s === 201), 'ten files in all: taken');
r = await call('PUT', `${P}/file/${C.b64u(C.rand(8))}`, { token: k.send, body: C.rand(80), headers: { 'x-chunks': '1' } });
check(r.status === 413 && r.json.code === 'SC-H-FILES', 'an eleventh: refused, SC-H-FILES');
// (50 MB: what is kept counted as it comes — a hand-in made full, then one more part)
const k3 = await C.derive(C.b64u(C.rand(32)));
const read3 = C.b64u(C.rand(32));
await call('PUT', `/${k3.id}`, { token: read3, body: { send: k3.send, offer: C.b64u(C.rand(64)) } });
const f3 = C.b64u(C.rand(8));
r = await call('PUT', `/${k3.id}/file/${f3}`, { token: k3.send, body: C.rand(80), headers: { 'x-chunks': '2' } });
db(k3.id).prepare('UPDATE hand SET used = ?').run(H.LIMITS.total - CH + 10);
r = await call('PUT', `/${k3.id}/file/${f3}/0`, { token: k3.send, body: junk(CH + 28) });
check(r.status === 413 && r.json.code === 'SC-H-413', 'a part that would take the hand-in past 50 MB: refused, SC-H-413');
r = await call('PUT', `/${k3.id}/file/${C.b64u(C.rand(8))}`, { token: k3.send, body: C.rand(80), headers: { 'x-chunks': '3' } });
check(r.status === 413, 'and a file that cannot fit in what is left: refused before a part is sent');
const from = '10.9.9.9';
const starts = [];
for (let n = 0; n < H.LIMITS.startsPerHour + 1; n++) { const kk = await C.derive(C.b64u(C.rand(32))); starts.push((await call('PUT', `/${kk.id}`, { token: C.b64u(C.rand(32)), body: { send: kk.send, offer: C.b64u(C.rand(64)) }, from })).status); }
check(starts.slice(0, -1).every((s) => s === 201) && starts.at(-1) === 429, `${H.LIMITS.startsPerHour} hand-ins started from one address in an hour; one more: refused, SC-H-429`);

console.log('handin: closed, and handed in');
r = await call('DELETE', P, { token: k.send });
check(r.status === 403, 'the phone cannot close it');
const k4 = await C.derive(C.b64u(C.rand(32))), read4 = C.b64u(C.rand(32)), P4 = `/${k4.id}`;
await call('PUT', P4, { token: read4, body: { send: k4.send, offer: C.b64u(await C.seal(k4.key, C.aad(k4.id, 'offer'), enc.encode('{}'))) } });
const f4 = C.b64u(C.rand(8));
await call('PUT', `${P4}/file/${f4}`, { token: k4.send, body: C.rand(80), headers: { 'x-chunks': '1' } });
await call('PUT', `${P4}/file/${f4}/0`, { token: k4.send, body: C.rand(300) });
r = await call('DELETE', P4, { token: read4 });
const h4 = db(k4.id).prepare('SELECT * FROM hand').get();
check(r.status === 200 && h4.state === 'closed' && h4.offer === null && db(k4.id).prepare('SELECT COUNT(*) AS n FROM chunk').get().n === 0 && db(k4.id).prepare('SELECT meta FROM file').get().meta === null, 'the sheet closed: the offer and every file forgotten at once (one never taken too)');
r = await call('GET', `${P4}/offer`, { token: k4.send });
check(r.status === 410 && r.json.code === 'SC-H-410', 'the phone can no longer open it (SC-H-410)');
r = await call('PUT', `${P4}/file/${C.b64u(C.rand(8))}`, { token: k4.send, body: C.rand(80), headers: { 'x-chunks': '1' } });
check(r.status === 410, 'nor send to it');
r = await call('GET', `${P4}/status`, { token: k4.send });
check(r.status === 200 && r.json.state === 'closed', 'its look says closed on the computer');
r = await call('POST', `${P4}/status`, { token: read4, body: { state: 'open' } });
check(r.status === 400, 'a computer gives one state only: "handed-in"');
r = await call('POST', `${P4}/status`, { token: k4.send, body: { state: 'handed-in' } });
check(r.status === 403, 'and the phone cannot give it');
r = await call('POST', `${P4}/status`, { token: read4, body: { state: 'handed-in' } });
check(r.status === 200, 'Submit went through: handed in, after the sheet closed');
r = await call('GET', `${P4}/status`, { token: k4.send });
check(r.json.state === 'handed-in', 'and the phone is told ("Handed in ✓")');
r = await call('GET', P, { token: read });
const keptBefore = r.json.files.length;
r = await call('POST', `${P}/status`, { token: read, body: { state: 'handed-in' } });
r = await call('GET', `${P}/status`, { token: k.send });
check(r.json.state === 'handed-in' && keptBefore === 10 && keptBlobs({ objects: new Map([[k.id, objOf(k.id)]]) }).length === 0, 'handed in while still open: its files and offer forgotten, the state word kept');
r = await call('PUT', `${P}/file/${C.b64u(C.rand(8))}`, { token: k.send, body: C.rand(80), headers: { 'x-chunks': '1' } });
check(r.status === 410, 'and nothing more taken from the phone');

console.log('handin: forgotten after 30 minutes');
const k5 = await C.derive(C.b64u(C.rand(32))), read5 = C.b64u(C.rand(32)), P5 = `/${k5.id}`;
await call('PUT', P5, { token: read5, body: { send: k5.send, offer: C.b64u(C.rand(64)) } });
const t0 = now;
now += 30e3;
await call('GET', P5, { token: read5 });
check(db(k5.id).prepare('SELECT expires FROM hand').get().expires === t0 + 30 * 60e3, 'a look half a minute on writes nothing (kept to the minute)');
now = t0 + 20 * 60e3;
await call('GET', P5, { token: read5 });
check(db(k5.id).prepare('SELECT expires FROM hand').get().expires === now + 30 * 60e3, 'a look 20 minutes on keeps it 30 minutes from then');
now += 31 * 60e3;
r = await call('GET', P5, { token: read5 });
check(r.status === 404 && r.json.code === 'SC-H-404', 'nothing done with it for 31 minutes: nothing there, even before its alarm');
check(db(k5.id).prepare("SELECT COUNT(*) AS n FROM sqlite_master WHERE type = 'table'").get().n === 3 && db(k5.id).prepare('SELECT COUNT(*) AS n FROM hand').get().n === 0, 'and its storage emptied (the tables made again, empty, for whatever comes next)');
// the alarm itself (it reads the real clock: the time kept to is put in the past)
const k6 = await C.derive(C.b64u(C.rand(32))), read6 = C.b64u(C.rand(32));
await call('PUT', `/${k6.id}`, { token: read6, body: { send: k6.send, offer: C.b64u(C.rand(64)) } });
const o6 = objOf(k6.id);
db(k6.id).prepare('UPDATE hand SET expires = ?').run(Date.now() + 10 * 60e3);
await o6.obj.alarm();
check(o6.storage.alarm === db(k6.id).prepare('SELECT expires FROM hand').get().expires, 'an alarm that comes before its time sets itself again for then');
db(k6.id).prepare('UPDATE hand SET expires = ?').run(Date.now() - 1);
await o6.obj.alarm();
check(db(k6.id).prepare("SELECT COUNT(*) AS n FROM sqlite_master WHERE type = 'table'").get().n === 0 && o6.storage.alarm === null, 'its alarm at its time: everything forgotten');
r = await call('GET', `/${k6.id}`, { token: read6 });
check(r.status === 404, 'and asked after, nothing there');

console.log('handin: codes');
check(uncoded.length === 0, `every refusal carries a code (SC-H-…)${uncoded.length ? `: ${uncoded.join(', ')}` : ''}`);
const src = readFileSync(join(root, 'site', 'src', 'handin.js'), 'utf8') + readFileSync(join(root, 'site', 'src', 'handin-store.js'), 'utf8');
const used = new Set([...src.matchAll(/refuse\('?(\w+)'?/g)].map((m) => `SC-H-${m[1]}`).filter((c) => c !== 'SC-H-what'));
const docs = readFileSync(join(root, 'docs', 'ERROR-CODES.md'), 'utf8');
const pageSrc = readFileSync(join(root, 'site', 'public', 'handin', 'index.html'), 'utf8');
const pageCodes = new Set([...pageSrc.matchAll(/'(SC-H-[A-Z0-9]+)'/g)].map((m) => m[1]));
const missing = [...used, ...pageCodes].filter((c) => !docs.includes(c));
check(missing.length === 0 && used.size >= 10, `every code the relay and the phone's page give is in docs/ERROR-CODES.md (${[...used, ...pageCodes].join(' ')})${missing.length ? `: missing ${missing.join(' ')}` : ''}`);
check([...codes].every((c) => used.has(c)), `the codes met here are the ones the source gives: ${[...codes].join(' ')}`);

console.log('handin: the phone\'s page');
const script = pageSrc.match(/<script>([\s\S]*?)<\/script>/)[1];
const hash = createHash('sha256').update(script, 'utf8').digest('base64');
const csp = pageSrc.match(/http-equiv="Content-Security-Policy" content="([^"]+)"/)?.[1] || '';
check(csp.includes(`script-src 'sha256-${hash}'`), `its Content-Security-Policy allows its own script and nothing else (sha256-${hash})`);
check(/default-src 'none'/.test(csp) && /connect-src 'self'/.test(csp) && !/unsafe-inline'[^;]*script|script-src[^;]*unsafe/.test(csp), 'and lets it reach its own site alone');
check((pageSrc.match(/<script/g) || []).length === 1 && !/<link\b|\bsrc="http|url\(http/i.test(pageSrc), 'one inline script, no file fetched from anywhere');
const asks = [...script.matchAll(/(fetch|\.open)\(([^,)]+)/g)].map((m) => m[2].trim());
check(asks.length >= 2 && asks.every((a) => /API|'PUT'/.test(a)), `every request it makes is to /api/handin: ${asks.join(', ')}`);
check(new RegExp(`CHUNK = ${H.LIMITS.chunk}\\b`).test(script) && /'simpl-handin-v1'/.test(script) && /'simpl-handin\/1'/.test(script), 'it parts files as the relay takes them, and seals as the computer opens');
// (run as it is, in a bare page of stand-ins, opened from a link without its secret)
const els = new Map();
const el = (id) => { if (!els.has(id)) els.set(id, { id, hidden: false, disabled: false, textContent: '', addEventListener() {}, setAttribute() {}, style: {}, classList: { toggle() {}, add() {}, remove() {} } }); return els.get(id); };
vm.runInNewContext(script, { document: { getElementById: el, addEventListener() {}, hidden: false }, location: { hash: '#not-a-secret' }, window: {}, crypto: globalThis.crypto, TextEncoder, TextDecoder, setTimeout, clearTimeout, btoa, atob });
check(el('goneTitle').textContent === 'This link is incomplete' && el('gone').hidden === false && el('goneCode').textContent.includes('SC-H-LINK') && el('files').disabled === true, `its script runs, and a link cut short says so (SC-H-LINK) with nothing to send: "${el('goneTitle').textContent}"`);

console.log('handin: the photos\' PDF');
const jpeg = (w, h) => new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 0, 16, 0x4a, 0x46, 0x49, 0x46, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0, 0xff, 0xc0, 0, 17, 8, h >> 8, h & 255, w >> 8, w & 255, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1, 0xff, 0xd9]);
const tall = jpeg(1650, 2200), wide = jpeg(2200, 1650);
const ti = C.jpegInfo(tall);
check(ti && ti.w === 1650 && ti.h === 2200 && ti.comps === 3 && C.jpegInfo(new Uint8Array([1, 2, 3, 4])) === null && C.jpegInfo(enc.encode('%PDF-1.4')) === null, 'a JPEG\'s size and colours read from its frame header; anything else is no page');
const pdf = C.pdfOf([{ jpeg: tall, ...ti }, { jpeg: wide, ...C.jpegInfo(wide) }], { title: 'Lab 3: Forces — é' });
const text = Buffer.from(pdf).toString('latin1');
check(text.startsWith('%PDF-1.4') && text.trimEnd().endsWith('%%EOF') && /\/Count 2/.test(text) && (text.match(/\/Type \/Page /g) || []).length === 2, 'two photos, one PDF of two pages');
check(/MediaBox \[0 0 612 792\]/.test(text) && /MediaBox \[0 0 792 612\]/.test(text) && (text.match(/\/Filter \/DCTDecode/g) || []).length === 2, 'a tall photo on a Letter page, a wide one turned to landscape, each JPEG put in as it is');
const xrefAt = Number(/startxref\n(\d+)/.exec(text)[1]);
const offs = text.slice(xrefAt).split('\n').slice(3).filter((l) => / 00000 n $/.test(l)).map((l) => Number(l.slice(0, 10)));
check(text.slice(xrefAt, xrefAt + 4) === 'xref' && offs.length === 3 + 3 * 2 && offs.every((o, i) => text.slice(o, o + 12).startsWith(`${i + 1} 0 obj`)), 'its cross-reference table points at each object (a reader opens it without repair)');
check(/\/Title <FEFF[0-9A-F]+>/.test(text) && text.includes('00E9'), 'named after the assignment, in UTF-16');

console.log('handin: what the phone may send');
const rule = (allowed, convertible = []) => C.offerOf({ allowed, convertible }).photos;
check(rule([]) === 'yes' && rule(['jpg', 'pdf']) === 'yes' && rule(['pdf']) === 'pdf' && rule(['docx']) === 'no' && rule(['docx'], ['png']) === 'yes', 'photos as they are, made one PDF, or not taken — as the apps decide');
const o1 = C.offerOf({ allowed: ['pdf'], convertible: ['docx'], pdfName: 'X.pdf' });
check(o1.accept.includes('pdf') && o1.accept.includes('docx') && o1.accept.includes('jpg') && o1.pdfName === 'X.pdf', 'its picker takes the types, those converted to them, and photos where they become pages');
check(C.offerOf({ allowed: ['zip', 'pdf'], zipOK: true }).accept.length === 0 && C.offerOf({ allowed: [] }).accept.length === 0, 'anything, where any file goes (no types named, or one zipped)');
check(C.cleanName('../../etc/pass\u0000wd') === 'passwd' && C.cleanName('') === 'From your phone' && C.cleanName(`${'a'.repeat(200)}.pdf`).length === 120 && C.cleanName(`${'a'.repeat(200)}.pdf`).endsWith('.pdf'), 'a name from the phone made safe: no folders, nothing unprintable, 120 characters at most');
const bg = readFileSync(join(root, 'extension', 'background.js'), 'utf8');
const bgPath = new RegExp(/const HANDIN_PATH = \/(.*)\/;/.exec(bg)[1]);
check([P, `${P}/offer`, `${P}/status`, F, `${F}/0`, `${F}/99`].every((p) => bgPath.test(`/api/handin${p}`)) && ![`${P}/../feed`, `/api/feed/${k.id}`, `${F}/01`, `${P}/x`].some((p) => bgPath.test(p.startsWith('/api') ? p : `/api/handin${p}`)), 'the background sends the relay\'s own paths and nothing else');

console.log(failures.length ? `\n${failures.length} check(s) failed:\n${failures.map((x) => ` - ${x}`).join('\n')}` : '\nall passed');
process.exit(failures.length ? 1 : 0);

#!/usr/bin/env node
// The request layer on its own (extension/lib/canvas-api.js, run in a sandbox with a fake fetch):
// the gate — at most ten GETs in flight, the newest navigation's requests served first when a
// slot frees, writes never held, a warm-up in flight giving its slot up to the screen being drawn
// and asked again later, a queued warm-up the new screen wants moved up to its place — a
// throttled 403 asked again rather than thrown, a refusal still thrown, and a request that never
// answers given up, asked once more, then failed.
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
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

console.log(fails ? `\n${fails} check(s) failed` : '\nAll checks passed.');
process.exit(fails ? 1 : 0);

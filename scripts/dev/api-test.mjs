#!/usr/bin/env node
// The request layer on its own (extension/lib/canvas-api.js, run in a sandbox with a fake fetch):
// the gate — at most ten GETs in flight, the newest navigation's requests served first when a
// slot frees, writes never held — a throttled 403 asked again rather than thrown, a refusal still
// thrown, and a request that never answers given up, asked once more, then failed.
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

console.log(fails ? `\n${fails} check(s) failed` : '\nAll checks passed.');
process.exit(fails ? 1 : 0);

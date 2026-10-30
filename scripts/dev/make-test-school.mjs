#!/usr/bin/env node
// The iPhone app's test school (test.simpl.com): the mock Canvas the suites run (scripts/dev/mock-canvas.mjs) made into a
// script a web page can run, so the app carries a whole Canvas of its own — courses, work, grades, quizzes, discussions,
// the Inbox, files, a Knewton Alta stand-in — with no server anywhere. App Review signs in to it (it has no school
// account), and so can anyone trying the app.
//
// The mock is a Node http server; the page has no Node. What the mock takes from Node is stood in for, small and
// exact (every use is checked below, so a change to the mock that reaches for something more fails here, not on a
// phone): http.createServer (the request handler, kept), server.listen (nothing), the request (method, url, headers,
// its body through on('data') / on('end')), the response (writeHead, setHeader, statusCode, write, end with a string
// or bytes), Buffer (from a string as latin1 or UTF-8, byteLength) and process (argv and env). The page then offers
// window.__mockHandle(method, url, headersJSON, bodyBase64) → Promise<{ status, headers, bodyBase64 }>, which the app
// calls for every request its web views make to simpltest://test.simpl.com (ios/SimplCourses/Web/TestSchool.swift),
// and window.__mockLoginPage(error, user), the mock's own Canvas sign-in page retitled for the test school.
//
// The mock writes its own address into some answers (http://localhost:8800/… — an upload's address, a tool's launch,
// the history — and a second origin's, :8801, for a module's link to somebody else's site): every text answer has
// both turned into the test school's own address, simpltest://test.simpl.com, before it leaves the page. And a Canvas
// page (one with ENV) gets the session's CSRF token as a csrf-token meta tag: the mock hands it over in a cookie, and a
// custom scheme keeps no cookies, so the interface finds it where Canvas's own front end looks next (lib/canvas-api.js).
//
// The output is committed (the iPhone build has no Node step) and CI fails when it is not what this writes
// (.github/workflows/ios.yml): run this after changing the mock, and commit what it writes.
//
//   node scripts/dev/make-test-school.mjs               # writes ios/SimplCourses/Resources/TestSchool/{mock.js,index.html}
//   node scripts/dev/make-test-school.mjs --out <dir>   # writes them there instead (scripts/dev/test-school-test.mjs)
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const args = process.argv.slice(2);
const outAt = args.indexOf('--out');
const outDir = outAt >= 0 && args[outAt + 1] ? resolve(args[outAt + 1]) : join(root, 'ios', 'SimplCourses', 'Resources', 'TestSchool');

// the ports the mock is made with (its own, and the second origin's) and the address that takes their place
export const PORT = 8800;
export const SIM_PORT = 8801;
export const ORIGIN = 'simpltest://test.simpl.com';

const fail = (why) => { console.error(`make-test-school: ${why}`); process.exit(1); };
let src = readFileSync(join(root, 'scripts', 'dev', 'mock-canvas.mjs'), 'utf8').replace(/\r\n/g, '\n');

// ---- what the mock may take from Node: exactly what is stood in for below ----------------------------------------
const IMPORT = "import http from 'node:http';";
if (!src.includes(IMPORT)) fail(`the mock no longer starts with ${IMPORT} — look at what it imports now`);
src = src.replace(/^#!.*\n/, '').replace(IMPORT, '// (node:http: stood in for above)');
// (Macmillan's stand-in pages, scripts/dev/mock-macmillan.mjs: two strings of HTML, put in where the mock imports them)
const MM_IMPORT = "import { MACMILLAN_HUB_HTML, MACMILLAN_SAC_HTML } from './mock-macmillan.mjs';";
if (src.includes(MM_IMPORT)) {
  const mm = readFileSync(join(root, 'scripts', 'dev', 'mock-macmillan.mjs'), 'utf8').replace(/\r\n/g, '\n').replace(/^export const /gm, 'const ');
  src = src.replace(MM_IMPORT, () => `// (mock-macmillan.mjs, put in here)\n${mm}`);
}
// (Mac 1.3.198) ALEKS's stand-in page, scripts/dev/mock-aleks.mjs: a string of HTML, put in where the mock imports it
const AK_IMPORT = "import { ALEKS_LEARN_HTML } from './mock-aleks.mjs';";
if (src.includes(AK_IMPORT)) {
  const ak = readFileSync(join(root, 'scripts', 'dev', 'mock-aleks.mjs'), 'utf8').replace(/\r\n/g, '\n').replace(/^export const /gm, 'const ');
  src = src.replace(AK_IMPORT, () => `// (mock-aleks.mjs, put in here)\n${ak}`);
}
// (the website's made-up names, scripts/dev/site-names.mjs, are for the website's pictures: the test school never turns
// them on, so it gets a stand-in that leaves every answer as it is)
const SN_IMPORT = "import { renameNames } from './site-names.mjs';";
src = src.replace(SN_IMPORT, () => 'const renameNames = (text) => text; // (site-names.mjs: the website\'s pictures only)');
// (Simpl Community's stand-in, scripts/dev/mock-community.mjs, is the website's: the test school has none)
const CM_IMPORT = "import { communityMock } from './mock-community.mjs'; // (2.99.106: Simpl Community, as simplcourses.com has it)";
src = src.replace(CM_IMPORT, () => 'const communityMock = null; // (mock-community.mjs: the website\'s Simpl Community, not the test school\'s)');
// (2.99.110: and the stand-in for Simpl's calendar feed, scripts/dev/mock-feed.mjs, the website's too)
const FD_IMPORT = "import { feedMock } from './mock-feed.mjs'; // (2.99.110: Simpl's own calendar feed, as simplcourses.com has it)";
src = src.replace(FD_IMPORT, () => 'const feedMock = null; // (mock-feed.mjs: the website\'s calendar feed, not the test school\'s)');
// (the test school is no real school: the mock's few words of the university it was first drawn from — an address, a
// notice's title, a mascot's greeting — are made the test school's own, here only; CI's mock keeps them)
src = src.replaceAll('ucmerced.edu', 'test.simpl.com').replaceAll('UC Merced', 'Simpl Test School').replaceAll('Hello Bobcat!', 'Hello!').replaceAll('Bobcat', 'Student');
for (const word of ['merced', 'bobcat']) if (src.toLowerCase().includes(word)) throw new Error(`the test school still names a real school: "${word}"`);
if (/^\s*(import|export)\b/m.test(src)) fail('the mock imports or exports something more than node:http: stand it in for here');
const uses = (re) => [...new Set([...src.matchAll(re)].map((m) => m[1]))].sort();
const only = (what, found, allowed) => {
  const extra = found.filter((x) => !allowed.includes(x));
  if (extra.length) fail(`the mock uses ${what}${extra.join(`, ${what}`)}, which the page has no stand-in for`);
};
only('Buffer.', uses(/\bBuffer\.(\w+)/g), ['byteLength', 'from']);
only('process.', uses(/\bprocess\.(\w+)/g), ['argv', 'env']);
only('http.', uses(/\bhttp\.(\w+)/g), ['createServer']);
only('server.', uses(/\bserver\.(\w+)/g), ['listen']);
only('req.', uses(/\breq\.(\w+)/g), ['headers', 'method', 'on', 'url']);
only('res.', uses(/\bres\.(\w+)/g), ['end', 'setHeader', 'statusCode', 'write', 'writeHead']);
for (const g of ['require(', '__dirname', '__filename', 'global.', 'setImmediate', 'queueMicrotask(', 'import.meta']) {
  if (src.includes(g)) fail(`the mock uses ${g}, which the page has no stand-in for`);
}
// what the page's own side below reads of the mock's
for (const need of ['function loginPage(', "const CSRF = '", 'const esc = (s)', 'http.createServer((req, res) =>', "Mock University", 'class="ic-Login__note"']) {
  if (!src.includes(need)) fail(`the mock no longer has ${need} — the page's side below reads it`);
}

const header = `// GENERATED by scripts/dev/make-test-school.mjs from scripts/dev/mock-canvas.mjs: do not edit it here. Change the mock,
// then run \`node scripts/dev/make-test-school.mjs\` and commit what it writes (CI checks the two agree).
//
// The iPhone app's test school (test.simpl.com): the mock Canvas, run by a hidden page in the app
// (ios/SimplCourses/Web/TestSchool.swift), answering every request the app's web views make to ${ORIGIN}.
`;

// ---- the stand-ins for Node, before the mock -----------------------------------------------------------------------
const before = `(function () {
'use strict';
// ---- what the mock takes from Node -----------------------------------------------------------------------------------
const __school = { handler: null };
const process = { argv: ['node', 'mock-canvas.mjs', '${PORT}', '${SIM_PORT}'], env: {} };
const http = {
  createServer(handler) {
    __school.handler = handler;
    const server = { listen() { return server; }, on() { return server; }, close() { return server; } };
    return server;
  },
};
const __enc = new TextEncoder();
/** Node's Buffer, as far as the mock goes: bytes made from a string (UTF-8, or latin1: a byte a character), and a string's length in bytes. */
class Buffer extends Uint8Array {
  static from(value, encoding) {
    if (typeof value === 'string') {
      if (encoding === 'latin1' || encoding === 'binary') {
        const b = new Buffer(value.length);
        for (let i = 0; i < value.length; i++) b[i] = value.charCodeAt(i) & 0xff;
        return b;
      }
      if (encoding && encoding !== 'utf8' && encoding !== 'utf-8') throw new Error('Buffer.from: no ' + encoding + ' here');
      const u = __enc.encode(value);
      const b = new Buffer(u.length);
      b.set(u);
      return b;
    }
    const u = value instanceof ArrayBuffer ? new Uint8Array(value) : Uint8Array.from(value);
    const b = new Buffer(u.length);
    b.set(u);
    return b;
  }
  static byteLength(value, encoding) {
    if (typeof value !== 'string') return value.byteLength;
    if (encoding === 'latin1' || encoding === 'binary') return value.length;
    return __enc.encode(value).length;
  }
}
// ---- the mock Canvas, as the suites run it (scripts/dev/mock-canvas.mjs) ---------------------------------------------
`;

// ---- the page's side, after the mock -------------------------------------------------------------------------------
const after = `
// ---- the page's side: what the app calls ------------------------------------------------------------------------------
const ORIGIN = ${JSON.stringify(ORIGIN)};
const LOCAL = [${JSON.stringify(`http://localhost:${PORT}`)}, ${JSON.stringify(`http://localhost:${SIM_PORT}`)}];
const __dec = new TextDecoder();
/** An answer the page may read as text (and so has the mock's own addresses in it turned into the test school's). */
const TEXTY = /^(text\\/|application\\/(json|javascript|xml|xhtml\\+xml|x-www-form-urlencoded)|image\\/svg\\+xml)/i;
const local = (text) => LOCAL.reduce((t, from) => t.split(from).join(ORIGIN).split(encodeURIComponent(from)).join(encodeURIComponent(ORIGIN)), String(text));
const bytesOf = (chunk) => (typeof chunk === 'string' ? __enc.encode(chunk) : chunk instanceof Uint8Array ? chunk : chunk instanceof ArrayBuffer ? new Uint8Array(chunk) : __enc.encode(String(chunk)));
function joined(chunks) {
  const out = new Uint8Array(chunks.reduce((n, c) => n + c.length, 0));
  let at = 0;
  for (const c of chunks) { out.set(c, at); at += c.length; }
  return out;
}
function toBase64(bytes) {
  let s = '';
  for (let i = 0; i < bytes.length; i += 0x8000) s += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
  return btoa(s);
}
function fromBase64(text) {
  const s = atob(String(text || ''));
  const out = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) out[i] = s.charCodeAt(i);
  return out;
}
/** The answer as it leaves the page: no cookie (a custom scheme keeps none) and its own length; a text answer's
 *  addresses the test school's own, and a Canvas page carrying the session's CSRF token where the interface looks next. */
function adapt(status, headers, body) {
  const out = {};
  for (const [k, v] of Object.entries(headers)) {
    if (v === undefined || v === null || k === 'set-cookie' || k === 'content-length') continue;
    out[k] = Array.isArray(v) ? v.join(', ') : String(v);
  }
  if (out.location) out.location = local(out.location);
  let bytes = body;
  if (TEXTY.test(out['content-type'] || '')) {
    let text = local(__dec.decode(bytes));
    if (/^text\\/html/i.test(out['content-type']) && text.includes('ENV = ') && !text.includes('name="csrf-token"')) {
      text = text.replace(/<head>/i, (h) => h + '<meta name="csrf-token" content="' + esc(CSRF) + '">');
    }
    bytes = __enc.encode(text);
  }
  if (bytes.length) out['content-length'] = String(bytes.length);
  return { status, headers: out, bodyBase64: toBase64(bytes) };
}
/** One request, answered by the mock: its method, its path and query, its headers (as JSON) and its body (base64). */
window.__mockHandle = function (method, url, headersJSON, bodyBase64) {
  return new Promise((resolve) => {
    const headers = {};
    try { for (const [k, v] of Object.entries(JSON.parse(headersJSON || '{}') || {})) headers[String(k).toLowerCase()] = String(v); } catch (e) { /* none */ }
    const listeners = {};
    const req = { method: String(method || 'GET').toUpperCase(), url: String(url || '/'), headers, httpVersion: '1.1', on(event, fn) { (listeners[event] = listeners[event] || []).push(fn); return req; } };
    const chunks = [];
    const sent = {};
    let status = 200;
    let done = false;
    let timer = 0;
    const finish = (code, hdrs, body) => {
      if (done) return;
      done = true;
      clearTimeout(timer);
      resolve(adapt(code, hdrs, body));
    };
    const failed = (e) => { try { console.error('[test school]', e); } catch (x) { /* no console */ } finish(500, { 'content-type': 'text/plain; charset=utf-8' }, __enc.encode('The test school could not answer: ' + ((e && e.message) || e))); };
    const res = {
      headersSent: false,
      get statusCode() { return status; },
      set statusCode(v) { status = Number(v) || 200; },
      setHeader(k, v) { sent[String(k).toLowerCase()] = v; return res; },
      getHeader(k) { return sent[String(k).toLowerCase()]; },
      removeHeader(k) { delete sent[String(k).toLowerCase()]; },
      writeHead(code, message, hdrs) {
        status = Number(code) || 200;
        const h = message && typeof message === 'object' ? message : hdrs;
        for (const [k, v] of Object.entries(h || {})) sent[String(k).toLowerCase()] = v;
        res.headersSent = true;
        return res;
      },
      write(chunk) { if (chunk !== undefined && chunk !== null) chunks.push(bytesOf(chunk)); return true; },
      end(chunk) {
        if (typeof chunk === 'function') chunk = null;
        if (chunk !== undefined && chunk !== null) chunks.push(bytesOf(chunk));
        finish(status, sent, joined(chunks));
        return res;
      },
    };
    // (an answer the mock never finishes is given up, as a server that never answered)
    timer = setTimeout(() => finish(504, { 'content-type': 'text/plain; charset=utf-8' }, __enc.encode('The test school did not answer.')), 60000);
    try {
      __school.handler(req, res);
      const body = fromBase64(bodyBase64);
      if (body.length) { const text = __dec.decode(body); for (const fn of listeners.data || []) fn(text); }
      for (const fn of listeners.end || []) { const p = fn(); if (p && typeof p.catch === 'function') p.catch(failed); }
    } catch (e) {
      failed(e);
    }
  });
};
/** The mock's Canvas sign-in page as the test school's: its name, and a line about it in place of the mock's password. */
window.__mockLoginPage = function (error, user) {
  return loginPage({ error: String(error || ''), user: String(user || '') })
    .split('Mock University').join('Simpl Test School')
    .replace(/<p class="ic-Login__note">[\\s\\S]*?<\\/p>/, '<p class="ic-Login__note">Simpl’s test school: its courses and work are made up, and they stay on this device.</p>');
};
window.__mockReady = true;
})();
`;

const bundle = `${header}${before}${src.trimEnd()}\n${after}`;
const page = `<!doctype html>
<!-- GENERATED by scripts/dev/make-test-school.mjs: the hidden page the iPhone app runs its test school in (Web/TestSchool.swift). -->
<html><head><meta charset="utf-8"><title>Simpl Test School</title><script src="mock.js" charset="utf-8"></script></head><body></body></html>
`;

mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'mock.js'), bundle);
writeFileSync(join(outDir, 'index.html'), page);
console.log(`test school written to ${outDir} (mock.js ${Math.round(bundle.length / 1024)} KB)`);

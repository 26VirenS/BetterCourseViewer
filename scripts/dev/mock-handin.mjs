// (2.99.135) Hand in from your phone's relay, standing in for simplcourses.com's: the site's own handler
// (site/src/handin.js) over its HandInStore (site/src/handin-store.js), each hand-in's Durable Object storage a SQLite
// database in memory (node:sqlite, the same SQL Cloudflare runs), and the phone's page (site/public/handin/index.html) at
// /handin/. The mock Canvas answers with it on its own address (a Canvas on this computer uses its own relay:
// background.js handinBase), and a suite can run it as a server of its own on another port — another origin, as
// simplcourses.com is to a school's Canvas:
//
//   node scripts/dev/mock-handin.mjs 8818
//
// For the suites alone: GET /__mock/handin/kept (each hand-in: its state, its files, how many sealed bytes it keeps),
// POST /__mock/handin/grep (the body's bytes: whether any sealed thing kept holds them — it never should) and
// GET /__mock/handin/log (every address asked for, as the relay saw it: never a secret, which stays after #).
import http from 'node:http';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { handleHandIn } from '../../site/src/handin.js';
import { HandInStore } from '../../site/src/handin-store.js';

const root = new URL('../..', import.meta.url).pathname;
// (node:sqlite says it is experimental each time it loads: not said here, where it is only a stand-in)
const warn = process.emitWarning;
process.emitWarning = (w, ...rest) => (/SQLite/.test(String(w?.message || w)) ? undefined : warn.call(process, w, ...rest));
const { DatabaseSync } = await import('node:sqlite').finally(() => { process.emitWarning = warn; });

/** Durable Object storage as HandInStore uses it: sql.exec over SQLite (an ArrayBuffer bound and read back as a BLOB,
 *  as Cloudflare's does), an alarm, deleteAll. */
export function sqlStorage() {
  const db = new DatabaseSync(':memory:');
  let alarm = null;
  const bind = (v) => (v instanceof ArrayBuffer ? new Uint8Array(v) : v);
  const out = (row) => { const o = {}; for (const [k, v] of Object.entries(row)) o[k] = v instanceof Uint8Array ? v.buffer.slice(v.byteOffset, v.byteOffset + v.byteLength) : v; return o; };
  const sql = {
    exec(q, ...b) {
      const st = db.prepare(q);
      const rows = /^\s*(SELECT|PRAGMA|WITH)\b/i.test(q) ? st.all(...b.map(bind)).map(out) : (st.run(...b.map(bind)), []);
      return { toArray: () => rows, one: () => { if (rows.length !== 1) throw new Error('Expected exactly one result from SQL query'); return rows[0]; } };
    },
  };
  return {
    db, sql,
    get alarm() { return alarm; },
    async setAlarm(t) { alarm = Number(t); },
    async getAlarm() { return alarm; },
    async deleteAlarm() { alarm = null; },
    async deleteAll() { // (all of it, the tables too, as a SQLite-backed object's does; its alarm with it, from 2026-02-24)
      for (const { name } of db.prepare("SELECT name FROM sqlite_master WHERE type = 'table'").all()) db.exec(`DROP TABLE IF EXISTS "${name}"`);
      alarm = null;
    },
  };
}

/** A stand-in for the HANDIN namespace: one object per name, kept in memory. */
export function memoryNamespace(Store = HandInStore) {
  const objects = new Map();
  return {
    objects,
    idFromName: (n) => n,
    get(id) {
      if (!objects.has(id)) { const storage = sqlStorage(); objects.set(id, { storage, obj: new Store({ storage }) }); }
      const { obj } = objects.get(id);
      return { fetch: (u, init) => obj.fetch(new Request(u, init)) };
    },
  };
}

/** Every sealed thing a namespace keeps (the offers, the metas, the chunks), as bytes. */
export function keptBlobs(ns) {
  const blobs = [];
  for (const { storage } of ns.objects.values()) {
    const tables = new Set(storage.db.prepare("SELECT name FROM sqlite_master WHERE type = 'table'").all().map((t) => t.name));
    if (tables.has('hand')) for (const r of storage.db.prepare('SELECT offer FROM hand').all()) if (r.offer) blobs.push(r.offer);
    if (tables.has('file')) for (const r of storage.db.prepare('SELECT meta FROM file').all()) if (r.meta) blobs.push(r.meta);
    if (tables.has('chunk')) for (const r of storage.db.prepare('SELECT data FROM chunk').all()) blobs.push(r.data);
  }
  return blobs;
}
const holds = (hay, needle) => Buffer.from(hay).indexOf(Buffer.from(needle)) >= 0;

const HANDIN = memoryNamespace();
const seen = [];
const page = () => readFileSync(join(root, 'site', 'public', 'handin', 'index.html'));

/** The relay's addresses: /api/handin/*, /handin/ (the phone's page), /__mock/handin/*. True when it answered. */
export async function handinMock(req, res, url) {
  const chunks = [];
  for await (const c of req) chunks.push(c);
  const body = Buffer.concat(chunks);
  if (!url.pathname.startsWith('/__mock/')) { seen.push(`${req.method} ${req.url}`); if (seen.length > 5000) seen.splice(0, 1000); }
  if (url.pathname === '/__mock/handin/log') {
    res.writeHead(200, { 'content-type': 'application/json; charset=utf-8' });
    return void res.end(JSON.stringify({ ok: true, seen }));
  }
  if (url.pathname === '/handin' || url.pathname === '/handin/' || url.pathname === '/handin/index.html') {
    res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
    return void res.end(page());
  }
  if (url.pathname === '/__mock/handin/kept') {
    const kept = [...HANDIN.objects.entries()].map(([id, { storage }]) => {
      const t = new Set(storage.db.prepare("SELECT name FROM sqlite_master WHERE type = 'table'").all().map((x) => x.name));
      const hand = t.has('hand') ? storage.db.prepare('SELECT state, opened, used, files, offer IS NOT NULL AS offer FROM hand').all()[0] || null : null;
      const files = t.has('file') ? storage.db.prepare('SELECT f, chunks, got, taken, meta IS NOT NULL AS meta FROM file ORDER BY seq').all().map((x) => ({ ...x })) : [];
      const bytes = t.has('chunk') ? Number(storage.db.prepare('SELECT COALESCE(SUM(LENGTH(data)), 0) AS n FROM chunk').all()[0].n) : 0;
      return { id, hand: hand && { ...hand }, files, bytes, alarm: storage.alarm };
    });
    res.writeHead(200, { 'content-type': 'application/json; charset=utf-8' });
    return void res.end(JSON.stringify({ ok: true, kept }));
  }
  if (url.pathname === '/__mock/handin/grep' && req.method === 'POST') {
    res.writeHead(200, { 'content-type': 'application/json; charset=utf-8' });
    return void res.end(JSON.stringify({ ok: true, found: body.length > 0 && keptBlobs(HANDIN).some((b) => holds(b, body)) }));
  }
  const r = await handleHandIn(new Request(url.href, { method: req.method, headers: req.headers, body: ['GET', 'HEAD', 'OPTIONS'].includes(req.method) ? undefined : body, duplex: 'half' }), { HANDIN });
  res.writeHead(r.status, Object.fromEntries(r.headers));
  res.end(Buffer.from(await r.arrayBuffer()));
}
export const isHandinPath = (p) => /^\/(api\/handin|__mock\/handin)(\/|$)/.test(p) || /^\/handin(\/|\/index\.html)?$/.test(p);

// run on its own: a relay of its own on a port of its own
if (process.argv[1] && new URL(import.meta.url).pathname === process.argv[1]) {
  const port = Number(process.argv[2]) || 8818;
  http.createServer((req, res) => {
    const url = new URL(req.url, `http://localhost:${port}`);
    if (!isHandinPath(url.pathname)) { res.writeHead(404, { 'content-type': 'text/plain' }); return void res.end('Not found'); }
    handinMock(req, res, url).catch((e) => { res.writeHead(500); res.end(String(e?.message || e)); });
  }).listen(port, () => console.log(`relay on http://localhost:${port}`));
}

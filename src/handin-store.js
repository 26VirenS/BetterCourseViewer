// (2.99.135) One Hand in from your phone (src/handin.js), kept by a Durable Object of its own: the sealed offer, each
// file's sealed meta and sealed chunks (SQL rows, a BLOB each: a row holds 2 MB, a chunk is 512 KiB), the hashes of the
// two tokens, and a few plain facts — open, closed or handed in; when the phone opened the offer; which files have come
// whole and which the computer has taken. It cannot open anything it keeps: the key is never sent here.
//
// Forgotten 30 minutes after the last thing done with it (an alarm, and every question checks the time as well). A file
// the computer has taken is forgotten at once; closed or handed in, every file and the offer go at once, and the rest
// (no name, no byte of a file) stays until its 30 minutes are up, so the phone can still be told it was handed in.
// SQLite-backed (wrangler.jsonc migrations, "handin-v1"), the kind the Workers Free plan has.
import { LIMITS, json, bytes, refuse } from './handin.js';

const KEEP = LIMITS.keepMinutes * 60e3;
const STEP = 60e3; // (the time it is kept to is written again at most once a minute: a look every second writes nothing)

export class HandInStore {
  constructor(state) {
    this.storage = state.storage;
    this.sql = state.storage.sql;
    this.schema();
  }

  /** Its three tables (again after a wipe: deleteAll takes the tables too, and the object may stay in memory). */
  schema() {
    this.sql.exec(`CREATE TABLE IF NOT EXISTS hand (
      one INTEGER PRIMARY KEY CHECK (one = 1), read TEXT NOT NULL, send TEXT NOT NULL, offer BLOB, state TEXT NOT NULL,
      created INTEGER NOT NULL, expires INTEGER NOT NULL, opened INTEGER, used INTEGER NOT NULL DEFAULT 0, files INTEGER NOT NULL DEFAULT 0)`);
    this.sql.exec(`CREATE TABLE IF NOT EXISTS file (
      f TEXT PRIMARY KEY, seq INTEGER NOT NULL, chunks INTEGER NOT NULL, meta BLOB, got INTEGER NOT NULL DEFAULT 0, taken INTEGER NOT NULL DEFAULT 0)`);
    this.sql.exec('CREATE TABLE IF NOT EXISTS chunk (f TEXT NOT NULL, i INTEGER NOT NULL, data BLOB NOT NULL, size INTEGER NOT NULL, PRIMARY KEY (f, i))');
    this.ready = true;
  }

  rows(q, ...b) { return this.sql.exec(q, ...b).toArray(); }
  hand() { return this.rows('SELECT * FROM hand WHERE one = 1')[0] || null; }
  fileOf(f) { return this.rows('SELECT * FROM file WHERE f = ?', f)[0] || null; }
  /** Every file's sealed bytes and the offer forgotten (closed, handed in): what stays has no name and no byte of a file. */
  emptied() {
    this.sql.exec('DELETE FROM chunk');
    this.sql.exec('UPDATE file SET meta = NULL');
    this.sql.exec('UPDATE hand SET offer = NULL');
  }
  /** Still in use: kept another 30 minutes from now (written at most once a minute). */
  touch(h, now) {
    if (h.state === 'open' && now + KEEP - h.expires >= STEP) this.sql.exec('UPDATE hand SET expires = ? WHERE one = 1', now + KEEP);
  }
  async wipe() {
    this.ready = false;
    await this.storage.deleteAll();
    try { await this.storage.deleteAlarm(); } catch { /* (deleteAll takes the alarm too, from the compatibility date of 2026-02-24) */ }
  }

  async fetch(request) {
    const op = new URL(request.url).pathname.slice(1);
    const hash = request.headers.get('x-hash') || '';
    const role = request.headers.get('x-role') === 'send' ? 'send' : 'read';
    const now = Number(request.headers.get('x-now')) || Date.now();
    const f = request.headers.get('x-file') || '';
    const i = Number(request.headers.get('x-index') || 0);
    const body = request.method === 'POST' ? await request.arrayBuffer() : null; // (every byte read before anything is decided: what follows has no await until it is written)
    if (!this.ready) this.schema();
    let h = this.hand();
    if (h && now >= h.expires) { await this.wipe(); this.schema(); h = null; } // (its alarm may come a little late)

    if (op === 'create') {
      if (h) {
        if (h.read !== hash) return refuse(403);
        if (h.state !== 'open') return refuse(410);
        this.sql.exec('UPDATE hand SET offer = ? WHERE one = 1', body); // (the same start sent again: its answer was lost)
        return json({ ok: true, expires: h.expires });
      }
      const send = request.headers.get('x-send') || '';
      this.sql.exec('INSERT INTO hand (one, read, send, offer, state, created, expires) VALUES (1, ?, ?, ?, ?, ?, ?)', hash, send, body, 'open', now, now + KEEP);
      await this.storage.setAlarm(now + KEEP);
      return json({ ok: true, expires: now + KEEP }, 201);
    }

    if (!h) return refuse(404);
    if (h[role] !== hash) return refuse(403);
    const open = h.state === 'open';
    this.touch(h, now);

    switch (op) {
      case 'poll': { // the computer's look
        const files = this.rows('SELECT f, seq, chunks, got, taken FROM file ORDER BY seq').map((x) => ({ f: x.f, seq: x.seq, chunks: x.chunks, got: x.got, taken: !!x.taken }));
        return json({ ok: true, state: h.state, opened: !!h.opened, files, expires: Math.max(h.expires, open ? now + KEEP : 0) });
      }
      case 'close': // the sheet closed on the computer: the phone can send no more, and what it sent is forgotten
        this.emptied();
        if (open) this.sql.exec('UPDATE hand SET state = ?, expires = ? WHERE one = 1', 'closed', now + KEEP);
        return json({ ok: true });
      case 'handedIn': // Submit went through on the computer: the phone is told, and every file goes
        this.emptied();
        this.sql.exec('UPDATE hand SET state = ?, expires = ? WHERE one = 1', 'handed-in', Math.max(h.expires, now + KEEP));
        return json({ ok: true });
      case 'status': { // the phone's look
        const files = this.rows('SELECT f, chunks, got, taken FROM file ORDER BY seq').map((x) => ({ f: x.f, done: x.got >= x.chunks, taken: !!x.taken }));
        return json({ ok: true, state: h.state, files });
      }
      case 'offer':
        if (!open || !h.offer) return refuse(410);
        if (!h.opened) this.sql.exec('UPDATE hand SET opened = ? WHERE one = 1', now);
        return bytes(h.offer);
      case 'metaPut': {
        if (!open) return refuse(410);
        const chunks = Number(request.headers.get('x-chunks'));
        const had = this.fileOf(f);
        if (had) { // (the same description sent again — its answer was lost — is taken; another is not)
          const same = had.meta && had.chunks === chunks && equal(had.meta, body);
          return same ? json({ ok: true, f, seq: had.seq }) : refuse(409, 'That file has been described already.');
        }
        if (h.files >= LIMITS.files) return refuse('FILES');
        if (h.used + (chunks - 1) * LIMITS.chunk + 1 > LIMITS.total) return refuse(413); // (its parts, all but the last full, would not fit)
        const seq = h.files + 1;
        this.sql.exec('INSERT INTO file (f, seq, chunks, meta) VALUES (?, ?, ?, ?)', f, seq, chunks, body);
        this.sql.exec('UPDATE hand SET files = ? WHERE one = 1', seq);
        return json({ ok: true, f, seq }, 201);
      }
      case 'chunkPut': {
        if (!open) return refuse(410);
        const file = this.fileOf(f);
        if (!file) return refuse(404, 'Describe the file first.');
        if (i >= file.chunks) return refuse(409, 'That file has fewer parts.');
        if (file.taken || file.got >= file.chunks) return json({ ok: true, got: file.got, done: true }); // (its last part sent again, its answer lost: it is here already)
        if (i > file.got) return refuse(409, 'Out of order: send the parts one after another.');
        const n = body.byteLength - 28; // (its plain bytes: the nonce and the tag aside)
        if (n > LIMITS.chunk || (i < file.chunks - 1 && n !== LIMITS.chunk)) return refuse(400, `Every part but the last is ${LIMITS.chunk} bytes.`);
        const old = i < file.got ? this.rows('SELECT size FROM chunk WHERE f = ? AND i = ?', f, i)[0]?.size || 0 : 0; // (a part sent again: its answer was lost)
        if (h.used - old + n > LIMITS.total) return refuse(413);
        this.sql.exec('INSERT OR REPLACE INTO chunk (f, i, data, size) VALUES (?, ?, ?, ?)', f, i, body, n);
        const got = i === file.got ? file.got + 1 : file.got;
        if (got !== file.got) this.sql.exec('UPDATE file SET got = ? WHERE f = ?', got, f);
        this.sql.exec('UPDATE hand SET used = ? WHERE one = 1', h.used - old + n);
        return json({ ok: true, got, done: got >= file.chunks });
      }
      case 'metaGet': {
        const file = this.fileOf(f);
        return file?.meta ? bytes(file.meta) : refuse(404);
      }
      case 'chunkGet': {
        const c = this.rows('SELECT data FROM chunk WHERE f = ? AND i = ?', f, i)[0];
        return c ? bytes(c.data) : refuse(404);
      }
      case 'take': { // the computer has it: forgotten here at once
        const file = this.fileOf(f);
        if (!file) return refuse(404);
        this.sql.exec('DELETE FROM chunk WHERE f = ?', f);
        this.sql.exec('UPDATE file SET meta = NULL, taken = 1 WHERE f = ?', f);
        return json({ ok: true });
      }
      default:
        return refuse(405);
    }
  }

  async alarm() {
    if (!this.ready) this.schema();
    const h = this.hand();
    if (!h || Date.now() >= h.expires) await this.wipe();
    else await this.storage.setAlarm(h.expires);
  }
}

function equal(a, b) {
  const x = new Uint8Array(a), y = new Uint8Array(b);
  if (x.length !== y.length) return false;
  for (let k = 0; k < x.length; k++) if (x[k] !== y[k]) return false;
  return true;
}

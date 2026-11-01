// simplcourses.com/api/handin/* — (2.99.135) Hand in from your phone, for the extension: the phone sends its files to the
// computer, sealed, and they are added to the hand-in there as if chosen with Choose Files; the student looks them over
// and presses Submit on the computer. Nothing is ever handed in by itself. The Mac and Windows apps do the same over a
// server of their own on the Wi-Fi (mac/Simpl/Support/PhoneServer.swift); an extension cannot run one, so this relay
// passes the files between the two — and cannot read them.
//
// The computer makes one secret, s (32 random bytes), and shows it in a QR code: https://simplcourses.com/handin/#<s>.
// The fragment never reaches a server. From s both devices work out, with HKDF-SHA-256 (salt "simpl-handin-v1"):
//   id    (info "id", 16 bytes)    the hand-in's name here, in every path
//   key   (info "key", 32 bytes)   AES-256-GCM: everything that passes is sealed with it, a fresh 12-byte nonce each time
//   send  (info "send", 32 bytes)  the phone's token
// The computer's own token, read, is 32 random bytes it keeps (the phone never has it). Only the two tokens' hashes are
// kept here, and the key is never sent: what is kept is sealed bytes, opened only on the two devices.
//
// Each sealed piece is nonce (12) + ciphertext + tag (16), its associated data "simpl-handin/1|<id>|<what>" binding it to
// its place: "offer", "meta|<f>", "chunk|<f>|<i>" — a piece moved to another hand-in, file or place does not open.
//
//   PUT    /api/handin/<id>               read   start: {"send": <send token>, "offer": <the sealed offer, base64url>}
//   GET    /api/handin/<id>               read   the computer's look: {state, opened, files: [{f, seq, chunks, got, taken}]}
//   DELETE /api/handin/<id>               read   closed: the offer and every file forgotten (a word of state stays, below)
//   POST   /api/handin/<id>/status        read   {"state": "handed-in"}, once Submit has gone through
//   GET    /api/handin/<id>/offer         send   the sealed offer: the assignment, its course, what it takes (opened: marked)
//   GET    /api/handin/<id>/status        send   the phone's look: {state, files: [{f, done, taken}]}
//   PUT    /api/handin/<id>/file/<f>      send   a file's sealed meta (its name, type, size, chunk count), "x-chunks: <n>"
//   PUT    /api/handin/<id>/file/<f>/<i>  send   its chunk i (512 KiB of the file, sealed), in order: 0, 1, 2 …
//   GET    /api/handin/<id>/file/<f>[/<i>] read  the sealed meta, a sealed chunk
//   DELETE /api/handin/<id>/file/<f>      read   taken: the computer has it, so it is forgotten here at once
//   GET    /api/handin                           whether the store is there (the site's own check)
//
// <f> is a file's own id, 8 random bytes the phone makes. A file is whole when its last chunk has come; the computer
// fetches it, checks it against its meta, and takes it. One hand-in takes 10 files and 50 MB at most. It is forgotten 30
// minutes after the last thing done with it (an alarm); closed, or handed in, its files go at once, and what stays — the
// state word, the two hashes, which files arrived — goes 30 minutes later, so the phone can still say "Handed in ✓". Each
// hand-in is a Durable Object of its own (HandInStore, src/handin-store.js), which the deploy makes: nothing to set up first.
// No cookie is read (CORS is open: the tokens are the only proofs). Every refusal carries a code, SC-H-… (docs/ERROR-CODES.md).

export const LIMITS = {
  chunk: 512 * 1024, // a chunk's plain bytes (the last one of a file may be shorter); a SQL row holds 2 MB
  files: 10,
  total: 50 * 1024 * 1024, // the plain bytes of every file in one hand-in
  offer: 16 * 1024, // a sealed offer, at most
  meta: 4 * 1024, // a sealed meta, at most
  keepMinutes: 30,
  startsPerHour: 30, // hand-ins started from one address
};
export const SEAL = 12 + 16; // what sealing adds: the nonce and the tag
LIMITS.chunks = Math.ceil(LIMITS.total / LIMITS.chunk); // a file's chunks, at most
const ID = /^[A-Za-z0-9_-]{22}$/; // 16 bytes, base64url
const SECRET = /^[A-Za-z0-9_-]{43}$/; // 32 bytes, base64url (a token)
const FILE = /^[A-Za-z0-9_-]{11}$/; // 8 bytes, base64url
const INDEX = /^(0|[1-9]\d{0,3})$/;

export const cors = {
  'access-control-allow-origin': '*', // (no cookie is ever read: the tokens are the only proofs)
  'access-control-allow-methods': 'GET, PUT, POST, DELETE, OPTIONS',
  'access-control-allow-headers': 'authorization, content-type, x-chunks',
  'access-control-max-age': '86400',
};
export const json = (body, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store', ...cors } });
export const bytes = (data) => new Response(data, { status: 200, headers: { 'content-type': 'application/octet-stream', 'cache-control': 'no-store', 'x-robots-tag': 'noindex', ...cors } });

/** The words for each refusal, by its code's last part. */
export const WORDS = {
  400: 'That is not a sealed part of a hand-in.',
  401: 'This hand-in needs its token.',
  403: 'That is not this hand-in’s token.',
  404: 'Nothing here. The code may have expired: open Hand in from your phone again on your computer.',
  405: 'Not like that.',
  409: 'That part does not fit here.',
  410: 'This hand-in was closed on the computer.',
  413: 'That is more than one hand-in takes (50 MB).',
  FILES: 'One hand-in takes 10 files at most.',
  429: 'Too many hand-ins started from here for now. Try again in a while.',
  500: 'The hand-in could not be kept. Try again.',
  503: 'Hand in from your phone is not switched on at simplcourses.com yet.',
};
const STATUS = { FILES: 413 };
/** A refusal: its status, its code (SC-H-…) and its words. */
export const refuse = (what, message = WORDS[what]) => json({ ok: false, code: `SC-H-${what}`, message }, STATUS[what] || Number(what));

const hex = (buf) => [...new Uint8Array(buf)].map((x) => x.toString(16).padStart(2, '0')).join('');
export async function tokenHash(token) {
  return hex(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`simpl-handin:${token}`)));
}
export const fromB64url = (s) => {
  const t = String(s);
  const b = atob(t.replace(/-/g, '+').replace(/_/g, '/') + '='.repeat((4 - (t.length % 4)) % 4));
  const out = new Uint8Array(b.length);
  for (let i = 0; i < b.length; i++) out[i] = b.charCodeAt(i);
  return out;
};

// ---- a little memory per isolate: best effort, nothing stored -------------------------------------------------
const windows = new Map();
function tooMany(ip, now = Date.now()) {
  if (!ip) return false;
  const recent = (windows.get(ip) || []).filter((t) => now - t < 3600e3);
  recent.push(now);
  windows.set(ip, recent);
  if (windows.size > 5000) windows.clear();
  return recent.length > LIMITS.startsPerHour;
}

/** The hand-in's own Durable Object (env.HANDIN, the HandInStore namespace; the tests' stand-in answers the same). */
function store(env, id) {
  const ns = env.HANDIN;
  return ns.get(ns.idFromName(id));
}
/** One question for the store: `op`, the token's hash and role, the time, and any body. (A store that fails answers
 *  SC-H-500, coded as every refusal is.) */
async function ask(env, id, op, { hash, role, now, body, extra = {} }) {
  try {
    return await store(env, id).fetch(`https://handin/${op}`, { method: 'POST', headers: { 'x-hash': hash, 'x-role': role, 'x-now': String(now), ...extra }, body });
  } catch {
    return refuse(500);
  }
}

/** A body no larger than `max`: its bytes, or null (too large). */
async function bodyOf(request, max) {
  if (Number(request.headers.get('content-length') || 0) > max) return null;
  const b = await request.arrayBuffer();
  return b.byteLength > max ? null : b;
}

export async function handleHandIn(request, env, now = Date.now()) {
  if (request.method === 'OPTIONS') return new Response(null, { status: 204, headers: cors });
  const { pathname } = new URL(request.url);
  const parts = pathname.replace(/^\/api\/handin\/?/, '').split('/').filter(Boolean);
  if (!parts.length) return request.method === 'GET' ? json({ ok: true, store: !!env.HANDIN }) : refuse(405);
  if (!env.HANDIN) return refuse(503);
  const [id, what, f, i] = parts;
  if (!ID.test(id) || parts.length > 4) return refuse(404);
  const m = request.method;

  // which question this is, and whose token it needs
  let op = null, role = 'read';
  if (parts.length === 1) op = { PUT: 'create', GET: 'poll', DELETE: 'close' }[m];
  else if (what === 'status' && parts.length === 2) { op = { GET: 'status', POST: 'handedIn' }[m]; role = m === 'GET' ? 'send' : 'read'; }
  else if (what === 'offer' && parts.length === 2) { op = m === 'GET' ? 'offer' : null; role = 'send'; }
  else if (what === 'file' && FILE.test(f || '') && (parts.length === 3 || INDEX.test(i || ''))) {
    if (parts.length === 3) { op = { PUT: 'metaPut', GET: 'metaGet', DELETE: 'take' }[m]; role = m === 'PUT' ? 'send' : 'read'; }
    else { op = { PUT: 'chunkPut', GET: 'chunkGet' }[m]; role = m === 'PUT' ? 'send' : 'read'; }
  } else return refuse(404);
  if (!op) return refuse(405);

  const token = /^Bearer\s+(\S+)$/i.exec(request.headers.get('authorization') || '')?.[1] || '';
  if (!SECRET.test(token)) return refuse(401);
  const hash = await tokenHash(token);
  const extra = { 'x-file': f || '', 'x-index': i || '' };

  if (op === 'create') {
    if (tooMany(request.headers.get('cf-connecting-ip'), now)) return refuse(429);
    const raw = await bodyOf(request, LIMITS.offer * 2);
    if (!raw) return refuse(413, 'That offer is too big.');
    let body;
    try { body = JSON.parse(new TextDecoder().decode(raw)); } catch { return refuse(400, 'That is not a hand-in’s start.'); }
    const send = String(body?.send || '');
    let offer;
    try { offer = fromB64url(String(body?.offer || '')); } catch { offer = null; }
    if (!SECRET.test(send) || send === token) return refuse(400, 'A hand-in needs a token for the phone of its own.');
    if (!offer || offer.length <= SEAL || offer.length > LIMITS.offer) return refuse(400, 'That is not a sealed offer.');
    return ask(env, id, op, { hash, role, now, body: offer, extra: { 'x-send': await tokenHash(send) } });
  }
  if (op === 'handedIn') {
    let body;
    try { body = JSON.parse(new TextDecoder().decode((await bodyOf(request, 1024)) || new ArrayBuffer(0))); } catch { body = null; }
    if (body?.state !== 'handed-in') return refuse(400, 'The only state a computer gives is "handed-in".');
  }
  if (op === 'metaPut' || op === 'chunkPut') {
    const max = (op === 'metaPut' ? LIMITS.meta : LIMITS.chunk) + SEAL;
    const body = await bodyOf(request, max);
    if (!body) return refuse(413, op === 'metaPut' ? 'That file’s description is too big.' : 'That part of a file is too big.');
    if (body.byteLength <= SEAL) return refuse(400);
    const chunks = request.headers.get('x-chunks') || '';
    if (op === 'metaPut' && (!/^[1-9]\d{0,3}$/.test(chunks) || Number(chunks) > LIMITS.chunks)) return refuse(413, `A file is ${LIMITS.chunks} parts at most (50 MB).`);
    return ask(env, id, op, { hash, role, now, body, extra: { ...extra, 'x-chunks': chunks } });
  }
  return ask(env, id, op, { hash, role, now, extra });
}
